using System.Runtime.CompilerServices;
using System.Web;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace  Kreisverkehr.Jellyfin.Plugin.SatIp;

public class M3uParser
{
    public static async IAsyncEnumerable<Tuple<ChannelInfo, MediaSourceInfo>> EnumerateChannelsAsync(SatIpDevice device, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using(var httpClient = new HttpClient())
        using(TextReader reader = new StringReader(await httpClient.GetStringAsync(device.SatIpM3U, cancellationToken)))
        {
            string? firstLine = await reader.ReadLineAsync(cancellationToken);
            if(firstLine == null || !firstLine.StartsWith("#EXTM3U"))
                throw new InvalidDataException($"Invalid M3U file from device {device.FriendlyName} ({device.UniqueDeviceName}) at URL {device.SatIpM3U}");

            string? line;
            ChannelInfo? currentChannel = null;
            int channelNo = 0;
            while((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if(line.StartsWith("#EXTINF:"))
                {
                    channelNo++;
                    var channelName = line.Substring(line.IndexOf(',') + 1).Trim();
                    currentChannel = new ChannelInfo
                    {
                        Name = channelName,
                        Id = $"{device.UniqueDeviceName}/{HttpUtility.UrlEncode(channelName)}",
                        TunerHostId = device.UniqueDeviceName,
                        Number = channelNo.ToString()
                    };
                    continue;
                }

                if(line.StartsWith("#"))
                {
                    // ignore additional comments or tags
                    continue;
                }

                if(!string.IsNullOrWhiteSpace(line) && currentChannel != null)
                {
                    var isCodecTestChannel = string.Equals(currentChannel.Name, "ZDFinfo HD", StringComparison.OrdinalIgnoreCase);
                    var mediaSource = new MediaSourceInfo
                    {
                        Path = line.Trim(),
                        Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.Rtsp,
                        Container = "ts",
                        IgnoreDts = true,
                        Id = Guid.NewGuid().ToString(),
                        IsRemote = false,
                        RequiresOpening = true,
                        RequiresClosing = true,
                        IsInfiniteStream = true,
                        AnalyzeDurationMs = 3000,
                        SupportsProbing = false,
                        SupportsDirectStream = true,
                        SupportsDirectPlay = false,
                        SupportsTranscoding = true,
                        MediaStreams =
                        [
                            new MediaStream { Type = MediaStreamType.Video, Codec = isCodecTestChannel ? "h264" : null, Index = -1, IsInterlaced = !isCodecTestChannel },
                            new MediaStream { Type = MediaStreamType.Audio, Codec = isCodecTestChannel ? "ac3" : null, Index = -1 }
                        ]
                    };
                    yield return Tuple.Create(currentChannel, mediaSource);
                    currentChannel = null;
                }
            }
        }
    }
}