using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

public class EpgScanner : IEpgScanner
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(5);
    private readonly ILogger<EpgScanner> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly ISatIpTunerHost _tunerHost;

    public EpgScanner(ILogger<EpgScanner> logger, IConfigurationManager configurationManager, ISatIpTunerHost tunerHost)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _tunerHost = tunerHost;
    }

    public async IAsyncEnumerable<ProgramInfo> ScanAsync(Uri streamUri, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        if (endDateUtc <= startDateUtc)
            yield break;

        var pidsValue = HttpUtility.ParseQueryString(streamUri.Query)["pids"];
        var requestedPids = pidsValue?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var pid) ? pid : -1)
            .Where(pid => pid is >= 0 and <= 0x1fff)
            .ToHashSet() ?? [];
        if (requestedPids.Count == 0)
            throw new InvalidOperationException($"The SAT>IP URI does not contain a valid pids parameter. ({streamUri})");

        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(ScanDuration);
        var parser = new EitParser(requestedPids);
        parser.ProgramInfoDiscovered += (sender, args) =>
        {
            _logger.LogDebug("Discovered program info for channel {ChannelId}: {ProgramInfo}", channelId, args.ProgramInfo);
        };
        RtspSession? session = null;

        try
        {
            session = await RtspSession.OpenAsync(streamUri, scanCancellation.Token).ConfigureAwait(false);
            while (true)
            {
                var rtpPacket = await session.ReceiveRtpAsync(scanCancellation.Token).ConfigureAwait(false);

                var payload = rtpPacket.Payload.Span;
                for (var offset = 0; offset + 188 <= payload.Length; offset += 188)
                    parser.ProcessTransportPacket(payload.Slice(offset, 188));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }

        if (parser.MatchingServiceCount != 1)
        {
            _logger.LogWarning("Could not uniquely map SAT>IP PID list to a DVB service for channel {ChannelId}; found {ServiceCount} matches", channelId, parser.MatchingServiceCount);
            yield break;
        }

        var programs = parser.GetPrograms(channelId, startDateUtc, endDateUtc).ToList();
        if (programs.Count == 0)
            _logger.LogDebug("No DVB EIT events found in the requested time window for channel {ChannelId}", channelId);

        foreach (var program in programs)
        {
            yield return program;
        }
    }

    public async IAsyncEnumerable<ProgramInfo> ScanAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var mediaSources = await _tunerHost.GetChannelStreamMediaSources(channelId, cancellationToken);
        var mediaSource = mediaSources.FirstOrDefault();

        if (mediaSource == null)
        {
            _logger.LogWarning("No media source found for channel {ChannelId}", channelId);
            yield break;
        }

        if (!Uri.TryCreate(mediaSource.Path, UriKind.Absolute, out var streamUri) || !streamUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Channel {ChannelId} does not have a valid RTSP media source", channelId);
            yield break;
        }

        await foreach (var program in ScanAsync(streamUri, channelId, startDateUtc, endDateUtc, cancellationToken))
        {
            yield return program;
        }
    }
}