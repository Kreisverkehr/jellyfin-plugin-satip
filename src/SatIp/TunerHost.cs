using System.Collections.Concurrent;
using System.Web;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Kreisverkehr.Jellyfin.Plugin.SatIp.M3U;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;
using Kreisverkehr.NetUpnp;
using Kreisverkehr.NetUpnp.Model;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public interface ISatIpTunerHost : ITunerHost;

public class TunerHost : ISatIpTunerHost
{
    private const string SATIP_DEVICE_TYPE = "urn:ses-com:device:SatIPServer:1";
    private const char SATIP_DEVICE_ID_SEPARATOR = '/';
    private const string SATIP_TUNER_HOST_TYPE = "satip";
    private readonly ILogger<TunerHost> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly IUpnpDeviceCollection<SatIpDescription> _upnpDeviceCollection;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<SatIpDevice> _satipDevices;
    private readonly IEnumerable<TunerHostInfo> _tunerHostInfos;
    private readonly IUpnpClient _upnpClient;
    private readonly ISatIpLiveStreamFactory _liveStreamFactory;
    private readonly ConcurrentDictionary<string, (ChannelInfo, MediaSourceInfo)> _channels = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _cachedDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _channelCacheLoaded;

    public string Name => "SAT>IP Tuner";

    public string Type => SATIP_TUNER_HOST_TYPE;

    public bool IsSupported => true;

    public TunerHost(ILogger<TunerHost> logger, IConfigurationManager configurationManager, IUpnpDeviceCollection<SatIpDescription> upnpDeviceCollection, IUpnpClient upnpClient, IServiceProvider serviceProvider, ISatIpLiveStreamFactory liveStreamFactory)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _upnpClient = upnpClient;
        _liveStreamFactory = liveStreamFactory;
        _upnpDeviceCollection = upnpDeviceCollection;
        _serviceProvider = serviceProvider;
        _satipDevices = _upnpDeviceCollection
            .Select(d => d.Item2.Device as SatIpDevice)
            .Where(d => d?.DeviceType?.Equals(SATIP_DEVICE_TYPE, StringComparison.OrdinalIgnoreCase) ?? false)
            .Select(d => d!);
        _tunerHostInfos =
            from satIpDevice in _satipDevices
            from satIpRes in satIpDevice.SatIpCapabilities?.Split(',') ?? ["-1"]
            select CreateTunerHostInfo(satIpDevice, satIpRes);

    }

    public async Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
    {
        await _upnpClient.RunDiscoverDevicesAsync(SATIP_DEVICE_TYPE, discoveryDurationMs / 1000, waitForResponses: true, cancellationToken);

        return _tunerHostInfos.ToList();
    }

    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        RegisterListingsProvider();
        var configuredDeviceIds = GetConfiguredDeviceIds();
        if (!enableCache || !_channelCacheLoaded || !_cachedDeviceIds.SetEquals(configuredDeviceIds))
        {
            await FillChannelCache(configuredDeviceIds, cancellationToken);
        }

        return _channels.Values.Select(t => t.Item1).ToList();
    }

    private void RegisterListingsProvider()
    {
        var options = _configurationManager.GetLiveTvOptions();
        if (options is null)
        {
            _logger.LogMissingLiveTvConfigurationStore();
            return;
        }

        if (options.ListingProviders?.Any(p => string.Equals(p.Type, "satip", StringComparison.OrdinalIgnoreCase)) ?? false)
            return;

        var listingsMgr = _serviceProvider.GetRequiredService<IListingsManager>();
        listingsMgr.SaveListingProvider(new ListingsProviderInfo
        {
            Type = "satip",
            EnableAllTuners = false,
            EnabledTuners = GetConfiguredDeviceIds().ToArray(),
        }, false, false);
    }

    private async Task FillChannelCache(HashSet<string> configuredDeviceIds, CancellationToken cancellationToken)
    {
        _channelCacheLoaded = false;

        if (configuredDeviceIds.Count == 0)
        {
            _cachedDeviceIds = configuredDeviceIds;
            _channelCacheLoaded = true;
            _logger.LogNoTunerHostsConfigured();
            return;
        }

        var configuredServerIds = configuredDeviceIds
            .Select(GetSatIpServerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<Task> discoveryTasks = new();
        foreach (var serverId in configuredServerIds)
        {
            if (_satipDevices.Any(device => string.Equals(device.UniqueDeviceName, serverId, StringComparison.OrdinalIgnoreCase)))
                continue;

            _logger.LogUnknownSatIpServer(serverId);
            discoveryTasks.Add(_upnpClient.RunDiscoverDevicesAsync(serverId, waitForResponses: true, cancellationToken: cancellationToken));
        }
        await Task.WhenAll(discoveryTasks);

        foreach (var serverId in configuredServerIds.Where(serverId =>
            !_satipDevices.Any(device => string.Equals(device.UniqueDeviceName, serverId, StringComparison.OrdinalIgnoreCase))))
        {
            _logger.LogSatIpServerDiscoveryFailed(serverId);
        }

        foreach (var device in _satipDevices.Where(d => configuredServerIds.Contains(d.UniqueDeviceName)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(device.SatIpM3U) && Plugin.GetConfiguration().AdditionalPlaylistUris.Count == 0)
            {
                _logger.LogMissingM3uUrl(device.UniqueDeviceName);
                continue;
            }

            foreach (var playlistLocation in Plugin.GetConfiguration().AdditionalPlaylistUris)
                await FillChannelCacheFromM3U(playlistLocation, device, GetADeviceAddressOrHostname(device), cancellationToken);

            if (!string.IsNullOrWhiteSpace(device.SatIpM3U) && !Plugin.GetConfiguration().IgnoreServerPlaylist)
                await FillChannelCacheFromM3U(device.SatIpM3U, device, cancellationToken: cancellationToken);
        }

        _cachedDeviceIds = configuredDeviceIds;
        _channelCacheLoaded = true;
    }

    private async Task FillChannelCacheFromM3U(string playlistLocation, SatIpDevice device, string? replacementHost = null, CancellationToken cancellationToken = default)
    {
        _logger.LogLoadingM3uPlaylist(playlistLocation, device.UniqueDeviceName);
        try
        {
            using (var m3uParser = await M3uParser.CreateAsync(new Uri(playlistLocation)))
            {
                var playlist = m3uParser.Parse();
                foreach (var entry in playlist.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if(Plugin.GetConfiguration().ReplacePlaylistHostWithTunerHost && !string.IsNullOrWhiteSpace(replacementHost))
                        entry.MediaUri = ReplaceHost(entry.MediaUri, replacementHost);

                    var channelInfo = CreateChannelInfo(entry, device);
                    var mediaSourceInfo = CreateMediaSourceInfo(entry, device);
                    _channels[channelInfo.Id] = (channelInfo, mediaSourceInfo);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogM3uPlaylistLoadFailed(exception, playlistLocation, device.UniqueDeviceName);
            throw;
        }

        Uri ReplaceHost(Uri original, string newHostName) =>
            new UriBuilder(original.ToString())
            {
                Host = newHostName
            }.Uri;
    }

    private string? GetADeviceAddressOrHostname(UpnpDevice device) =>
        _upnpDeviceCollection
        .Where(d => d.Item2.Device.UniqueDeviceName == device.UniqueDeviceName)
        .Select(d => d.Item1.Host)
        .FirstOrDefault();

    private HashSet<string> GetConfiguredDeviceIds()
    {
        var liveTvOptions = _configurationManager.GetLiveTvOptions();
        if (liveTvOptions is null)
        {
            _logger.LogMissingLiveTvConfigurationStore();
            return [];
        }

        return liveTvOptions?.TunerHosts?
            .Where(t => string.Equals(t.Type, SATIP_TUNER_HOST_TYPE, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.DeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    }

    private static string GetSatIpServerId(string deviceId)
    {
        var separatorIndex = deviceId.IndexOf(SATIP_DEVICE_ID_SEPARATOR);
        return separatorIndex < 0 ? deviceId : deviceId[..separatorIndex];
    }

    public Task<ILiveStream> GetChannelStream(string channelId, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        return GetChannelStreamInternal(channelId, streamId, currentLiveStreams, cancellationToken);
    }

    private async Task<ILiveStream> GetChannelStreamInternal(string channelId, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        await EnsureChannelCacheLoaded(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_channels.TryGetValue(channelId, out var channelTuple))
            throw new ArgumentException($"Channel '{channelId}' was not found.", nameof(channelId));

        var (channel, mediaSource) = channelTuple;
        var sharedStream = currentLiveStreams.FirstOrDefault(liveStream =>
            liveStream.EnableStreamSharing
            && string.Equals(liveStream.TunerHostId, channel.TunerHostId, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(liveStream.OriginalStreamId, streamId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(liveStream.MediaSource.Id, mediaSource.Id, StringComparison.OrdinalIgnoreCase)));

        return sharedStream ?? _liveStreamFactory.Create(mediaSource, channel.TunerHostId, streamId);
    }

    private async Task EnsureChannelCacheLoaded(CancellationToken cancellationToken)
    {
        var configuredDeviceIds = GetConfiguredDeviceIds();
        if (!_channelCacheLoaded || !_cachedDeviceIds.SetEquals(configuredDeviceIds))
            await FillChannelCache(configuredDeviceIds, cancellationToken);
    }

    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        await EnsureChannelCacheLoaded(cancellationToken);

        return _channels.TryGetValue(channelId, out var channelTuple)
            ? [channelTuple.Item2]
            : [];
    }

    private static TunerHostInfo CreateTunerHostInfo(SatIpDevice device, string satIpRes)
    {
        var capabilityParts = satIpRes.Split('-', 2);
        var modulationSystem = capabilityParts[0];
        var friendlyName = string.IsNullOrWhiteSpace(modulationSystem)
            ? device.FriendlyName
            : $"{device.FriendlyName} ({modulationSystem})";

        return new TunerHostInfo
        {
            DeviceId = $"{device.UniqueDeviceName}{SATIP_DEVICE_ID_SEPARATOR}{modulationSystem}",
            FriendlyName = friendlyName,
            Url = device.PresentationUrl,
            Source = modulationSystem,
            Type = SATIP_TUNER_HOST_TYPE,
            TunerCount = int.Parse(capabilityParts[1]),
        };
    }

    private static ChannelInfo CreateChannelInfo(M3uEntry entry, SatIpDevice device) => new()
    {
        Id = $"{device.UniqueDeviceName}/{entry.MediaUri.ModulationSystem}/{entry.TvgId}",
        Name = entry.Title,
        Number = entry.TvgChno >= 0 ? entry.TvgChno.ToString() : string.Empty,
        ImageUrl = entry.TvgLogo,
        HasImage = !string.IsNullOrWhiteSpace(entry.TvgLogo),
        TunerHostId = $"{device.UniqueDeviceName}/{entry.MediaUri.ModulationSystem}",
    };

    private static MediaSourceInfo CreateMediaSourceInfo(M3uEntry entry, SatIpDevice device) => new()
    {
        Id = $"{device.UniqueDeviceName}/{entry.MediaUri.ModulationSystem}/{entry.TvgId}".ToMd5Guid().ToString(),
        Path = entry.MediaUri.ToString(),
        Protocol = entry.MediaUri.MediaProtocol,
        Container = "ts",
        IgnoreDts = true,
        RequiresOpening = true,
        RequiresClosing = true,
        IsInfiniteStream = true,
        AnalyzeDurationMs = 3000,
        SupportsProbing = false,
        SupportsDirectStream = true,
        SupportsDirectPlay = true,
        SupportsTranscoding = true,
    };
}