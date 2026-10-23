using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Controller;
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
    private readonly IServerApplicationHost _applicationHost;

    public SatIpLiveStreamFactory(IRtspSessionFactory rtspSessionFactory, IServerApplicationHost applicationHost)
    {
        _rtspSessionFactory = rtspSessionFactory;
        _applicationHost = applicationHost;
    }

    public ILiveStream Create(MediaSourceInfo mediaSource, string tunerHostId, string originalStreamId) =>
        new SatIpLiveStream(_rtspSessionFactory, _applicationHost, mediaSource, tunerHostId, originalStreamId);
}
