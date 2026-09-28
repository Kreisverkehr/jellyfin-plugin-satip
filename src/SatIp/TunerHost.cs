using System.Collections.Concurrent;
using System.Web;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;
using Kreisverkehr.NetUpnp;
using Kreisverkehr.NetUpnp.Model;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class TunerHost : ITunerHost
{
    private const string SATIP_DEVICE_TYPE = "urn:ses-com:device:SatIPServer:1";
    private const char SATIP_DEVICE_ID_SEPARATOR = '/';
    private readonly ILogger<TunerHost> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly IUpnpDeviceCollection _upnpDeviceCollection;
    private readonly IEnumerable<SatIpDevice> _satipDevices;
    private readonly IEnumerable<TunerHostInfo> _tunerHostInfos;
    private readonly IUpnpClient _upnpClient;
    private readonly ConcurrentDictionary<string, Tuple<ChannelInfo, MediaSourceInfo>> _channels = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _cachedDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _channelCacheLoaded;

    public string Name => "SAT>IP Tuner";

    public string Type => "SAT>IP";

    public bool IsSupported => true;

    public TunerHost(ILogger<TunerHost> logger, IConfigurationManager configurationManager, IUpnpDeviceCollection upnpDeviceCollection, IUpnpClient upnpClient)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _upnpClient = upnpClient;
        _upnpDeviceCollection = upnpDeviceCollection;
        _satipDevices = _upnpDeviceCollection
            .OfType<SatIpDevice>()
            .Where(d => d.DeviceType.Equals(SATIP_DEVICE_TYPE, StringComparison.OrdinalIgnoreCase));
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
        var configuredDeviceIds = GetConfiguredDeviceIds();
        if (!enableCache || !_channelCacheLoaded || !_cachedDeviceIds.SetEquals(configuredDeviceIds))
        {
            await FillChannelCache(configuredDeviceIds, cancellationToken);
        }

        return _channels.Values.Select(t => t.Item1).ToList();
    }

    private async Task FillChannelCache(HashSet<string> configuredDeviceIds, CancellationToken cancellationToken)
    {
        _channelCacheLoaded = false;
        _channels.Clear();

        if (configuredDeviceIds.Count == 0)
        {
            _cachedDeviceIds = configuredDeviceIds;
            _channelCacheLoaded = true;
            _logger.LogInformation("No SAT>IP tuner hosts are configured");
            return;
        }

        var configuredServerIds = configuredDeviceIds
            .Select(GetSatIpServerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var serverId in configuredServerIds)
        {
            if (_satipDevices.Any(device => string.Equals(device.UniqueDeviceName, serverId, StringComparison.OrdinalIgnoreCase)))
                continue;

            _logger.LogInformation("SAT>IP server {DeviceId} is not known; searching for its UDN", serverId);
            await _upnpClient.RunDiscoverDevicesAsync(serverId, waitForResponses: true, cancellationToken: cancellationToken);
        }

        foreach (var device in _satipDevices.Where(d => configuredServerIds.Contains(d.UniqueDeviceName)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(device.SatIpM3U))
            {
                _logger.LogWarning("Device {Device} has no M3U URL", device.UniqueDeviceName);
                continue;
            }

            await ReadChannelsFromM3U(device, cancellationToken);
        }

        _cachedDeviceIds = configuredDeviceIds;
        _channelCacheLoaded = true;
    }

    private HashSet<string> GetConfiguredDeviceIds()
    {
        var liveTvOptions = _configurationManager.GetLiveTvOptions();
        if (liveTvOptions is null)
        {
            _logger.LogWarning("Jellyfin Live TV configuration store was not found");
            return [];
        }

        return liveTvOptions?.TunerHosts?
            .Where(t =>
                string.Equals(t.Type, "satip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Type, "SAT>IP", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.DeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    }

    private static string GetSatIpServerId(string deviceId)
    {
        var separatorIndex = deviceId.IndexOf(SATIP_DEVICE_ID_SEPARATOR);
        return separatorIndex < 0 ? deviceId : deviceId[..separatorIndex];
    }

    private async Task ReadChannelsFromM3U(SatIpDevice device, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing channels for device {DeviceName} ({DeviceId}) from given M3U URL {M3UUrl}", device.FriendlyName, device.UniqueDeviceName, device.SatIpM3U);
        await foreach (var (channel, mediaSource) in M3uParser.EnumerateChannelsAsync(device, cancellationToken))
        {
            _channels[channel.Id] = Tuple.Create(channel, mediaSource);
        }
    }

    public Task<ILiveStream> GetChannelStream(string channelId, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("SAT>IP media sources are played directly without opening an ILiveStream.");
    }

    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        var configuredDeviceIds = GetConfiguredDeviceIds();
        if (!_channelCacheLoaded || !_cachedDeviceIds.SetEquals(configuredDeviceIds))
            await FillChannelCache(configuredDeviceIds, cancellationToken);

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
            Url = device.ModelUrl,
            Source = "SAT>IP",
            Type = "satip",
            TunerCount = int.Parse(capabilityParts[1]),
        };
    }
}