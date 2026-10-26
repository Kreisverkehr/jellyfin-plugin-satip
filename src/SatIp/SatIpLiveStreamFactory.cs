using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public interface ISatIpLiveStreamFactory
{
    ILiveStream Create(MediaSourceInfo mediaSource, string tunerHostId, string originalStreamId);
}

public sealed class SatIpLiveStreamFactory : ISatIpLiveStreamFactory
{
    private readonly IRtspSessionFactory _rtspSessionFactory;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ILoggerFactory _loggerFactory;

    public SatIpLiveStreamFactory(IRtspSessionFactory rtspSessionFactory, IServerApplicationHost applicationHost, ILoggerFactory loggerFactory)
    {
        _rtspSessionFactory = rtspSessionFactory;
        _applicationHost = applicationHost;
        _loggerFactory = loggerFactory;
    }

    public ILiveStream Create(MediaSourceInfo mediaSource, string tunerHostId, string originalStreamId) =>
        new SatIpLiveStream(_rtspSessionFactory, _applicationHost, _loggerFactory.CreateLogger<SatIpLiveStream>(), _loggerFactory.CreateLogger<SatIpLiveStream.RtpPayloadStream>(), mediaSource, tunerHostId, originalStreamId);
}
