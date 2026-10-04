using MediaBrowser.Controller.LiveTv;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public interface IEpgScanner
{
    IAsyncEnumerable<ProgramInfo> ScanAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken);
}