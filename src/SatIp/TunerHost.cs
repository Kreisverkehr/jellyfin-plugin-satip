using System.Collections.Concurrent;
using System.Web;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;
using Kreisverkehr.NetUpnp;
using Kreisverkehr.NetUpnp.Model;
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
    private readonly ILogger<TunerHost> _logger;
    private readonly IUpnpDeviceCollection _upnpDeviceCollection;
    private readonly IEnumerable<SatIpDevice> _satipDevices;
    private readonly IEnumerable<TunerHostInfo> _tunerHostInfos;
    private readonly IUpnpClient _upnpClient;
    private readonly ConcurrentDictionary<string, Tuple<ChannelInfo, MediaSourceInfo>> _channels = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "SAT>IP Tuner";

    public string Type => "SAT>IP";

    public bool IsSupported => true;

    public TunerHost(ILogger<TunerHost> logger, IUpnpDeviceCollection upnpDeviceCollection, IUpnpClient upnpClient)
    {
        _logger = logger;
        _upnpClient = upnpClient;
        _upnpDeviceCollection = upnpDeviceCollection;
        _satipDevices = _upnpDeviceCollection
            .OfType<SatIpDevice>()
            .Where(d => d.DeviceType.Equals(SATIP_DEVICE_TYPE, StringComparison.OrdinalIgnoreCase));
        _tunerHostInfos =
            from satIpDevice in _satipDevices
            from satIpRes in satIpDevice.SatIpCapabilities?.Split(',') ?? ["-1"]
            select CreateTunerHostInfo(satIpDevice, satIpRes);

        _upnpClient.RunDiscoverDevicesAsync(SATIP_DEVICE_TYPE).GetAwaiter().GetResult();
    }

    public async Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
    {
        await _upnpClient.RunDiscoverDevicesAsync(SATIP_DEVICE_TYPE, discoveryDurationMs / 1000, waitForResponses: true, cancellationToken);

        return _tunerHostInfos.ToList();
    }

    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        if (!enableCache || _channels.IsEmpty)
        {
            await FillChannelCache(cancellationToken);
        }

        return _channels.Values.Select(t => t.Item1).ToList();
    }

    private async Task FillChannelCache(CancellationToken cancellationToken)
    {
        await _upnpClient.RunDiscoverDevicesAsync(SATIP_DEVICE_TYPE, waitForResponses: true, cancellationToken: cancellationToken);

        foreach (var device in _satipDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(device.SatIpM3U))
            {
                _logger.LogWarning("Device {Device} has no M3U URL", device.UniqueDeviceName);
                continue;
            }

            await ReadChannelsFromM3U(device, cancellationToken);
        }
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
        if (_channels.IsEmpty)
            await FillChannelCache(cancellationToken);

        return _channels.TryGetValue(channelId, out var channelTuple)
            ? [channelTuple.Item2]
            : [];
    }

    private static TunerHostInfo CreateTunerHostInfo(SatIpDevice device, string satIpRes) => new()
    {
        Id = device.UniqueDeviceName + "/" + satIpRes,
        DeviceId = device.UniqueDeviceName,
        FriendlyName = device.FriendlyName,
        Url = device.ModelUrl,
        Source = "SAT>IP",
        Type = "satip",
        TunerCount = int.Parse(satIpRes.Split('-')[1]),
    };
}