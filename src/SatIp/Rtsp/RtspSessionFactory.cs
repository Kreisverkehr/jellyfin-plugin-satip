using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;

public interface IRtspSessionFactory
{
    Task<RtspSession> OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

public class RtspSessionFactory:IRtspSessionFactory
{
    private readonly ILogger<RtspSessionFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public RtspSessionFactory(ILogger<RtspSessionFactory> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<RtspSession> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        return await RtspSession.OpenAsync(uri, _loggerFactory.CreateLogger<RtspSession>(), cancellationToken);
    }
}