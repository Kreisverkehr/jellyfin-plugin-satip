using System.Collections.Specialized;
using System.Web;
using MediaBrowser.Model.MediaInfo;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;

public static class UriExtensions
{
    extension(Uri uri)
    {
        public MediaProtocol MediaProtocol => uri.Scheme.ToLowerInvariant() switch
        {
            "http" => MediaProtocol.Http,
            "https" => MediaProtocol.Http,
            "rtsp" => MediaProtocol.Rtsp,
            "rtp" => MediaProtocol.Rtp,
            "udp" => MediaProtocol.Udp,
            _ => throw new ArgumentException($"Unsupported URI scheme: {uri.Scheme}", nameof(uri)),
        };

        public NameValueCollection QueryParameters => HttpUtility.ParseQueryString(uri.Query);

        public string ModulationSystem => uri.QueryParameters["msys"] ?? string.Empty;
    }
}