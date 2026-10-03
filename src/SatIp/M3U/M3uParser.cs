using System.Runtime.CompilerServices;
using System.Web;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.M3U;

public class M3uParser : IDisposable
{
    private readonly TextReader _reader;

    public static async Task<M3uParser> CreateAsync(Uri playlistUri)
    {
        if (playlistUri == null)
            throw new ArgumentNullException(nameof(playlistUri));
        if (!playlistUri.IsAbsoluteUri)
            throw new ArgumentException("Playlist URI must be absolute", nameof(playlistUri));
        if (!playlistUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !playlistUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Playlist URI must be HTTP or HTTPS", nameof(playlistUri));

        using var httpClient = new HttpClient();
        var playlistContent = await httpClient.GetStringAsync(playlistUri);
        return new M3uParser(new StringReader(playlistContent));
    }

    private M3uParser(TextReader reader)
    {
        _reader = reader;
    }

    public M3uPlaylist Parse()
    {
        var playlist = new M3uPlaylist();
        int chNo = 1;
        string? line;
        M3uEntry? currentEntry = null;
        while ((line = _reader.ReadLine()) != null)
        {
            if(line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                ParseHeaderProperties(line, playlist.HeaderProperties);
                continue;
            }

            if(line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                currentEntry = new M3uEntry();
                ParseEntryHeader(line, currentEntry);
                continue;
            }

            if(line.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                if(currentEntry != null)
                {
                    ParseCustomProperties(line, currentEntry.CustomProperties);
                }
                continue;
            }

            if(line.Length > 0 && currentEntry != null)
            {
                currentEntry.MediaUri = new Uri(line);
                if(currentEntry.TvgChno < 0)
                {
                    currentEntry.HeaderProperties["tvg-chno"] = [chNo.ToString()];
                    chNo++;
                }
                playlist.Entries.Add(currentEntry);
                currentEntry = null;
            }

            /// Ignore empty lines
            /// If none of the above conditions are met, the line is ignored.
        }

        return playlist;
    }

    private void ParseCustomProperties(string line, Dictionary<string, Dictionary<string, List<string>>> customProperties)
    {
        var customPropertyParts = line.Substring(1).Split(':', 2);
        if (customPropertyParts.Length != 2)
            return;

        if (!customProperties.ContainsKey(customPropertyParts[0]))
            customProperties.Add(customPropertyParts[0], []);
        
        ParseKeyValuePairs(customPropertyParts[1], customProperties[customPropertyParts[0]]);
    }

    private void ParseEntryHeader(string line, M3uEntry currentEntry)
    {
        var properties = line.Split(':', ',');
        currentEntry.Duration = int.Parse(properties[1].Split(' ')[0]);
        currentEntry.Title = properties[2];
        ParseKeyValuePairs(properties[1].Substring(properties[1].IndexOf(' ') + 1), currentEntry.HeaderProperties);
    }

    private void ParseHeaderProperties(string line, Dictionary<string, List<string>> headerProperties)
    {
        var properties = line.Substring("#EXTM3U".Length).Trim();
        if (string.IsNullOrEmpty(properties))
            return;

        ParseKeyValuePairs(properties, headerProperties);
    }

    private void ParseKeyValuePairs(string line, Dictionary<string, List<string>> properties)
    {
        var keyValuePairs = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var kvp in keyValuePairs)
        {
            var parts = kvp.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0];
                var value = parts[1];
                if (!properties.ContainsKey(key))
                    properties[key] = [];
                properties[key].Add(value);
            }
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}