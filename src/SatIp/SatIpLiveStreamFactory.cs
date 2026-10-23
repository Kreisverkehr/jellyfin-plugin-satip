using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public interface ISatIpLiveStreamFactory
{
    ILiveStream Create(MediaSourceInfo mediaSource, string tunerHostId, string originalStreamId);
}

public sealed class SatIpLiveStreamFactory : ISatIpLiveStreamFactory
{
    private readonly IRtspSessionFactory _rtspSessionFactory;

    public SatIpLiveStreamFactory(IRtspSessionFactory rtspSessionFactory)
    {
        _rtspSessionFactory = rtspSessionFactory;
    }

    public ILiveStream Create(MediaSourceInfo mediaSource, string tunerHostId, string originalStreamId) =>
        new SatIpLiveStream(_rtspSessionFactory, mediaSource, tunerHostId, originalStreamId);
}
