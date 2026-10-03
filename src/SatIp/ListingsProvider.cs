using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public interface ISatIpListingsProvider : IListingsProvider;

public class ListingsProvider : ISatIpListingsProvider
{
    private readonly ILogger<ListingsProvider> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly ISatIpTunerHost _tunerHost;

    public string Name => "SAT>IP";

    public string Type => "satip";

    public ListingsProvider(ILogger<ListingsProvider> logger, IConfigurationManager configurationManager, ISatIpTunerHost tunerHost)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _tunerHost = tunerHost;
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

    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        var mediaSources = await _tunerHost.GetChannelStreamMediaSources(channelId, cancellationToken);
        var mediaSource = mediaSources.FirstOrDefault();

        if (mediaSource == null)
        {
            _logger.LogWarning("No media source found for channel {ChannelId}", channelId);
            return Enumerable.Empty<ProgramInfo>();
        }

        // TODO: Implement actual program guide retrieval logic here. For now, we return an empty list.
        return Enumerable.Empty<ProgramInfo>();
    }

    public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
    {
        return Task.CompletedTask;
    }
}