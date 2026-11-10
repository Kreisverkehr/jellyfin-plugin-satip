using System.Net;
using System.Net.Sockets;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;

public interface IRtpPortAllocator
{
    RtpPortLease Allocate();
}

public sealed class RtpPortAllocator : IRtpPortAllocator
{
    private readonly object _sync = new();

    public RtpPortLease Allocate()
    {
        var configuration = Plugin.GetConfiguration();
        var startPort = configuration.UdpPortRangeStart;
        var endPort = configuration.UdpPortRangeEnd;
        ValidateRange(startPort, endPort);

        lock (_sync)
        {
            for (var rtpPort = startPort; rtpPort < endPort; rtpPort += 2)
            {
                UdpClient? rtpClient = null;
                UdpClient? rtcpClient = null;

                try
                {
                    rtpClient = new UdpClient(AddressFamily.InterNetwork);
                    rtpClient.Client.Bind(new IPEndPoint(IPAddress.Any, rtpPort));
                    rtcpClient = new UdpClient(AddressFamily.InterNetwork);
                    rtcpClient.Client.Bind(new IPEndPoint(IPAddress.Any, rtpPort + 1));
                    return new RtpPortLease(rtpClient, rtcpClient);
                }
                catch (SocketException exception)
                {
                    rtcpClient?.Dispose();
                    rtpClient?.Dispose();

                    if (exception.SocketErrorCode is not (SocketError.AddressAlreadyInUse or SocketError.AccessDenied))
                        throw;
                }
                catch
                {
                    rtcpClient?.Dispose();
                    rtpClient?.Dispose();
                    throw;
                }
            }
        }

        throw new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    private static void ValidateRange(int startPort, int endPort)
    {
        if (startPort is < 49152 or > 65534
            || (startPort & 1) != 0
            || endPort is < 49153 or > 65535
            || (endPort & 1) == 0
            || startPort >= endPort)
        {
            throw new InvalidOperationException(
                $"The configured RTP/RTCP UDP port range {startPort}-{endPort} is invalid. " +
                "Ports must be within 49152-65535; the range must start with an even RTP port and end with an odd RTCP port, with at least one complete pair.");
        }
    }
}

public sealed class RtpPortLease : IDisposable
{
    private int _disposed;

    internal RtpPortLease(UdpClient rtpClient, UdpClient rtcpClient)
    {
        RtpClient = rtpClient;
        RtcpClient = rtcpClient;
    }

    public UdpClient RtpClient { get; }

    public UdpClient RtcpClient { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            RtpClient.Dispose();
        }
        finally
        {
            RtcpClient.Dispose();
        }
    }
}