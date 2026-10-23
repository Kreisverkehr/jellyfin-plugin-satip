using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Web;
using Cinegy.TsDecoder.Tables;
using Cinegy.TsDecoder.TransportStream;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

public class EpgScanner : IEpgScanner
{
    private readonly ILogger<EpgScanner> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly ISatIpTunerHost _tunerHost;
    private readonly IProgramInfoFactory _programInfoFactory;
    private readonly IRtspSessionFactory _rtspSessionFactory;

    public EpgScanner(ILogger<EpgScanner> logger, IConfigurationManager configurationManager, ISatIpTunerHost tunerHost, IProgramInfoFactory programInfoFactory, IRtspSessionFactory rtspSessionFactory)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _tunerHost = tunerHost;
        _programInfoFactory = programInfoFactory;
        _rtspSessionFactory = rtspSessionFactory;
    }

    public async IAsyncEnumerable<ProgramInfo> ScanAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var mediaSources = await _tunerHost.GetChannelStreamMediaSources(channelId, cancellationToken);
        var mediaSource = mediaSources.FirstOrDefault();

        if (mediaSource == null)
        {
            _logger.LogNoMediaSource(channelId);
            yield break;
        }

        if (!Uri.TryCreate(mediaSource.Path, UriKind.Absolute, out var streamUri) || !streamUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInvalidRtspMediaSource(channelId);
            yield break;
        }

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

        // Setup TsDecoder and channel for discovered ProgramInfo
        var programInfoChannel = Channel.CreateUnbounded<ProgramInfo>();
        var serviceIds = new HashSet<ushort>();
        var decoder = new TsDecoder();
        decoder.TableChangeDetected += TableChangeDetectedHandler;

        var scanDuration = TimeSpan.FromSeconds(Math.Max(1, Plugin.GetConfiguration().EpgScanDurationSeconds));
        _ = RunScanAsync(decoder, streamUri, scanDuration, cancellationToken)
            .ContinueWith(_ => Cleanup(), cancellationToken);

        while (await programInfoChannel.Reader.WaitToReadAsync(cancellationToken))
            yield return await programInfoChannel.Reader.ReadAsync(cancellationToken);

        async void TableChangeDetectedHandler(object? sender, TableChangedEventArgs e)
        {
            if (e.TableType == TableType.Pat)
            {
                var count = Math.Min(decoder.ProgramAssociationTable.ProgramNumbers.Length, decoder.ProgramAssociationTable.Pids.Length);
                for (var index = 0; index < count; index++)
                {
                    var serviceId = decoder.ProgramAssociationTable.ProgramNumbers[index];
                    var pmtPid = decoder.ProgramAssociationTable.Pids[index];

                    if (serviceId != 0 && requestedPids.Contains(pmtPid))
                    {
                        serviceIds.Add(serviceId);
                        _logger.LogFoundServiceId(serviceId, pmtPid);
                    }
                }
            }

            if ((e.TableType == TableType.Eit || e.TableType == TableType.EitSchedule) && sender is EventInformationTableFactory tableFactory && tableFactory.LastChangedTable is not null)
            {
                var table = tableFactory.LastChangedTable;

                if (serviceIds.Contains(table.ServiceId))
                    await foreach (var item in _programInfoFactory.GetProgramInfoFromEit(table))
                        await programInfoChannel.Writer.WriteAsync(item);
            }
        }

        void Cleanup()
        {
            decoder.TableChangeDetected -= TableChangeDetectedHandler;
            programInfoChannel.Writer.Complete();
        }
    }

    private async Task RunScanAsync(TsDecoder decoder, Uri streamUri, TimeSpan scanDuration, CancellationToken cancellationToken)
    {
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(scanDuration);

        RtspSession? session = null;
        try
        {
            session = await _rtspSessionFactory.OpenAsync(streamUri, scanCancellation.Token).ConfigureAwait(false);
            while (true)
            {
                var rtpPacket = await session.ReceiveRtpAsync(scanCancellation.Token).ConfigureAwait(false);
                decoder.AddData(rtpPacket.Payload.ToArray());
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