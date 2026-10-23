using Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

public interface IEpgScanner
{
    IAsyncEnumerable<ProgramInfo> ScanAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken);
}