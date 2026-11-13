using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Web;
using Kreisverkehr.DvbMetadataDecoder;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

public class EpgScanner : IEpgScanner
{
    private readonly ILogger<EpgScanner> _logger;
    private readonly IProgramInfoFactory _programInfoFactory;
    private readonly IRtspSessionFactory _rtspSessionFactory;
    private readonly IDvbMetadataRepository _dvbMetadata;
    private readonly IDvbMetadataDecoder _decoder;

    public EpgScanner(ILogger<EpgScanner> logger, IProgramInfoFactory programInfoFactory, IRtspSessionFactory rtspSessionFactory, IDvbMetadataRepository dvbMetadata, IDvbMetadataDecoder decoder)
    {
        _logger = logger;
        _programInfoFactory = programInfoFactory;
        _rtspSessionFactory = rtspSessionFactory;
        _dvbMetadata = dvbMetadata;
        _decoder = decoder;
    }

    public async IAsyncEnumerable<ProgramInfo> ScanAsync(string channelId, Uri streamUri, DateTime startDateUtc, DateTime endDateUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (_ = _logger.BeginScope("epg scan for channel {channelId} with uri {streamUri}", channelId, streamUri))
        {
            _logger.LogScanningPrograms(channelId);
            await foreach (var program in ScanStreamAsync(streamUri, cancellationToken))
            {
                if (program.EndDate >= startDateUtc && program.StartDate <= endDateUtc)
                {
                    program.ChannelId = channelId;
                    _logger.LogDiscoveredProgram(program.Id, program.Name, program.StartDate, program.EndDate);
                    yield return program;
                }
            }
        }
    }

    private async IAsyncEnumerable<ProgramInfo> ScanStreamAsync(Uri streamUri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Get requested PIDs from the SAT>IP stream URI query parameters
        var pidsValue = HttpUtility.ParseQueryString(streamUri.Query)["pids"];
        var requestedPids = pidsValue?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var pid) ? pid : -1)
            .Where(pid => pid is >= 0 and <= 0x1fff)
            .ToHashSet() ?? [];
        if (requestedPids.Count == 0)
        {
            _logger.LogInvalidPidsParameter(streamUri);
            yield break;
        }

        if (_dvbMetadata.ProgramAssociationTable.TransportStreamId == 0)
        {
            var scanDuration = TimeSpan.FromSeconds(Math.Max(1, Plugin.GetConfiguration().EpgScanDurationSeconds));
            await RunScanMultiplexAsync(streamUri.GetMultiplexUri(), scanDuration, cancellationToken);
        }

        var requestedServiceId = _dvbMetadata.ProgramMapTables.FirstOrDefault(pmt => requestedPids.Contains(pmt.ProgramMapPid))?.ProgramNumber;
        if (requestedPids is not null && _dvbMetadata.ActualTransportStreamScheduleEventInformationTables.FirstOrDefault(t => t.ServiceId == requestedServiceId) is IEventInformationTable eit)
        {
            await foreach (var program in _programInfoFactory.GetProgramInfoFromEit(eit))
            {
                yield return program;
            }
        }
    }

    private async Task RunScanMultiplexAsync(Uri multiplexUri, TimeSpan scanDuration, CancellationToken cancellationToken)
    {
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(scanDuration);

        RtspSession? session = null;
        try
        {
            session = await _rtspSessionFactory.OpenAsync(multiplexUri, scanCancellation.Token).ConfigureAwait(false);
            while (true)
            {
                var rtpPacket = await session.ReceiveRtpAsync(scanCancellation.Token).ConfigureAwait(false);
                await _decoder.FeedAsync(rtpPacket.Payload);
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
    }
}