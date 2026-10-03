using System.Web;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.M3U;

public class M3uPlaylist
{
    public Dictionary<string, List<string>> HeaderProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<M3uEntry> Entries { get; } = [];

    public string TvgUrl => HeaderProperties.TryGetValue("x-tvg-url", out var values) && values.Count > 0 ? values[0] : string.Empty;
    public int? CatchupDays => HeaderProperties.TryGetValue("catchup-days", out var values) && values.Count > 0 && int.TryParse(values[0], out var days) ? days : null;
}

public class M3uEntry
{
    public Dictionary<string, List<string>> HeaderProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, List<string>>> CustomProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Title { get; set; } = string.Empty;
    public int Duration { get; set; } = -1;
    public Uri MediaUri { get; set; } = new Uri("about:blank");

    public string TvgId => HeaderProperties.TryGetValue("tvg-id", out var values) && values.Count > 0 ? values[0] : HttpUtility.UrlEncode(Title);
    public string TvgLogo => HeaderProperties.TryGetValue("tvg-logo", out var values) && values.Count > 0 ? values[0] : string.Empty;
    public string GroupTitle => HeaderProperties.TryGetValue("group-title", out var values) && values.Count > 0 ? values[0] : string.Empty;
    public int TvgChno => HeaderProperties.TryGetValue("tvg-chno", out var values) && values.Count > 0 && int.TryParse(values[0], out var chno) ? chno : -1;
}