using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public sealed class SatIpLiveStream : ILiveStream
{
    private readonly string _tunerHostId;

    public SatIpLiveStream(string tunerHostId)
    {
        _tunerHostId = tunerHostId ?? throw new ArgumentNullException(nameof(tunerHostId));
        UniqueId = Guid.NewGuid().ToString("N");
    }

    public int ConsumerCount { get; set; }

    public string OriginalStreamId { get; set; } = string.Empty;

    public string TunerHostId => _tunerHostId;

    public bool EnableStreamSharing => false;

    public MediaSourceInfo MediaSource { get; set; } = null!;

    public string UniqueId { get; }

    public Task Open(CancellationToken openCancellationToken)
    {
        openCancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task Close()
    {
        return Task.CompletedTask;
    }

    public Stream GetStream()
    {
        throw new NotSupportedException("SAT>IP channels are consumed directly from their RTSP media source.");
    }

    public void Dispose()
    {
    }
}
