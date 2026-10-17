using Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public interface ISatIpListingsProvider : IListingsProvider;

public class ListingsProvider : ISatIpListingsProvider
{
    private readonly ILogger<ListingsProvider> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly ISatIpTunerHost _tunerHost;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "SAT>IP";

    public string Type => "satip";

    public ListingsProvider(ILogger<ListingsProvider> logger, IConfigurationManager configurationManager, ISatIpTunerHost tunerHost, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _tunerHost = tunerHost;
        _serviceProvider = serviceProvider;
    }

    public Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
    {
        return _tunerHost.GetChannels(true, cancellationToken);
    }

    public Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
    {
        return Task.FromResult(new List<NameIdPair>
        {
            new NameIdPair
            {
                Name = "SAT>IP",
                Id = "satip"
            }
        });
    }

    public async Task<IEnumerable<MediaBrowser.Controller.LiveTv.ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        var epgScanner = _serviceProvider.GetRequiredKeyedService<IEpgScanner>("dvb");
        _logger?.LogStartEpgScan(channelId);
        var programs = await epgScanner.ScanAsync(channelId, startDateUtc, endDateUtc, cancellationToken)
            .Distinct()
            .OrderBy(p => p.StartDate)
            .ToListAsync(cancellationToken);
        _logger?.LogEndEpgScan(channelId, programs.Count);
        return programs;
    }

    public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
    {
        return Task.CompletedTask;
    }
}