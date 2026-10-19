using System.Net;
using System.Net.Sockets;
using System.Text;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
    
    public sealed class RtspSession : IAsyncDisposable
    {
        private readonly Uri _uri;
        private readonly ILogger? _logger;
        private readonly TcpClient _tcpClient;
        private NetworkStream? _controlStream;
        private readonly UdpClient _rtpClient;
        private readonly UdpClient _rtcpClient;
        private string? _sessionId;
        private TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(30);
        private CancellationTokenSource? _keepAliveCancellation;
        private Task? _keepAliveTask;
        private int _cseq;

        private NetworkStream ControlStream => _controlStream ?? throw new InvalidOperationException("The RTSP connection has not been opened.");

        private RtspSession(Uri uri, ILogger? logger, TcpClient tcpClient, UdpClient rtpClient, UdpClient rtcpClient)
        {
            _uri = uri;
            _logger = logger;
            _tcpClient = tcpClient;
            _rtpClient = rtpClient;
            _rtcpClient = rtcpClient;
        }

        public static async Task<RtspSession> OpenAsync(Uri uri, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            var (rtpClient, rtcpClient) = BindRtpPorts();
            var tcpClient = new TcpClient(AddressFamily.InterNetwork);
            var session = new RtspSession(uri, logger, tcpClient, rtpClient, rtcpClient);

            try
            {
                await tcpClient.ConnectAsync(uri.DnsSafeHost, uri.Port, cancellationToken).ConfigureAwait(false);
                session._controlStream = tcpClient.GetStream();
                var options = await session.SendRequestAsync("OPTIONS", null, cancellationToken).ConfigureAwait(false);
                EnsureSuccess(options, "OPTIONS");

                var clientPort = ((IPEndPoint)rtpClient.Client.LocalEndPoint!).Port;
                var setup = await session.SendRequestAsync("SETUP", new Dictionary<string, string>
                {
                    ["Transport"] = $"RTP/AVP;unicast;client_port={clientPort}-{clientPort + 1};mode=\"PLAY\"",
                }, cancellationToken).ConfigureAwait(false);
                EnsureSuccess(setup, "SETUP");
                if (setup.Headers.TryGetValue("Session", out var sessionHeader))
                    session.InitializeSession(sessionHeader);

                var playHeaders = session._sessionId is null
                    ? null
                    : new Dictionary<string, string> { ["Session"] = session._sessionId };
                var play = await session.SendRequestAsync("PLAY", playHeaders, cancellationToken).ConfigureAwait(false);
                EnsureSuccess(play, "PLAY");
                session.StartKeepAlive();
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<RtpPacket> ReceiveRtpAsync(CancellationToken cancellationToken)
        {
            var result = await _rtpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (!RtpPacket.TryParse(result.Buffer, out var packet))
                throw new InvalidDataException("The SAT>IP server sent an invalid RTP packet.");

            return packet;
        }

        public async ValueTask DisposeAsync()
        {
            _keepAliveCancellation?.Cancel();
            if (_keepAliveTask is not null)
                await _keepAliveTask.ConfigureAwait(false);
            _keepAliveCancellation?.Dispose();

            if (_sessionId is not null && _tcpClient.Connected)
            {
                try
                {
                    _logger?.LogRtspTeardownRequested(_uri);
                    using var teardownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var response = await SendRequestAsync("TEARDOWN", new Dictionary<string, string>
                    {
                        ["Session"] = _sessionId,
                    }, teardownTimeout.Token).ConfigureAwait(false);
                    EnsureSuccess(response, "TEARDOWN");
                }
                catch (Exception exception)
                {
                    _logger?.LogRtspTeardownFailed(exception, _uri);
                }
            }

            _rtpClient.Dispose();
            _rtcpClient.Dispose();
            _tcpClient.Dispose();
        }

        private void InitializeSession(string sessionHeader)
        {
            var parameters = sessionHeader.Split(';');
            _sessionId = parameters[0].Trim();

            foreach (var parameter in parameters.Skip(1))
            {
                var separator = parameter.IndexOf('=');
                if (separator <= 0
                    || !parameter[..separator].Trim().Equals("timeout", StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(parameter[(separator + 1)..].Trim(), out var timeoutSeconds)
                    || timeoutSeconds <= 0)
                    continue;

                _keepAliveInterval = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds / 2));
                break;
            }
        }

        private void StartKeepAlive()
        {
            if (_sessionId is null)
                return;

            _keepAliveCancellation = new CancellationTokenSource();
            _keepAliveTask = KeepAliveAsync(_keepAliveCancellation.Token);
        }

        private async Task KeepAliveAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(_keepAliveInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                        var response = await SendRequestAsync("OPTIONS", new Dictionary<string, string>
                        {
                            ["Session"] = _sessionId!,
                        }, requestTimeout.Token).ConfigureAwait(false);
                        EnsureSuccess(response, "OPTIONS keep-alive");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        _logger?.LogWarning(exception, "SAT>IP RTSP keep-alive to {StreamUri} failed", _uri);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task<RtspResponse> SendRequestAsync(string method, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            var request = new StringBuilder()
                .Append(method).Append(' ').Append(_uri.AbsoluteUri).Append(" RTSP/1.0\r\n")
                .Append("CSeq: ").Append(++_cseq).Append("\r\n")
                .Append("User-Agent: Jellyfin-SATIP\r\n");

            if (headers is not null)
            {
                foreach (var header in headers)
                    request.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            request.Append("\r\n");
            var bytes = Encoding.ASCII.GetBytes(request.ToString());
            await ControlStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<RtspResponse> ReadResponseAsync(CancellationToken cancellationToken)
        {
            var statusLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var statusParts = statusLine.Split(' ', 3);
            if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out var statusCode))
                throw new InvalidDataException("The SAT>IP server returned an invalid RTSP status line.");

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line.Length == 0)
                    break;

                var separator = line.IndexOf(':');
                if (separator > 0)
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            if (headers.TryGetValue("Content-Length", out var contentLengthText)
                && int.TryParse(contentLengthText, out var contentLength)
                && contentLength > 0)
            {
                var remaining = contentLength;
                var bodyBuffer = new byte[Math.Min(contentLength, 4096)];
                while (remaining > 0)
                {
                    var read = await ControlStream.ReadAsync(bodyBuffer.AsMemory(0, Math.Min(remaining, bodyBuffer.Length)), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        throw new EndOfStreamException("The SAT>IP server closed the RTSP connection.");
                    remaining -= read;
                }
            }

            return new RtspResponse(statusCode, headers);
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            var value = new byte[1];
            while (line.Count <= 8192)
            {
                var read = await ControlStream.ReadAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("The SAT>IP server closed the RTSP connection.");

                if (value[0] == '\n')
                    return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');

                line.Add(value[0]);
            }

            throw new InvalidDataException("The SAT>IP server returned an oversized RTSP header.");
        }

        private static (UdpClient Rtp, UdpClient Rtcp) BindRtpPorts()
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var rtpClient = new UdpClient(AddressFamily.InterNetwork);
                rtpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                var port = ((IPEndPoint)rtpClient.Client.LocalEndPoint!).Port;
                if ((port & 1) != 0 || port == 65534)
                {
                    rtpClient.Dispose();
                    continue;
                }

                try
                {
                    var rtcpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port + 1));
                    return (rtpClient, rtcpClient);
                }
                catch (SocketException)
                {
                    rtpClient.Dispose();
                }
            }

            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }

        private static void EnsureSuccess(RtspResponse response, string method)
        {
            if (response.StatusCode is < 200 or >= 300)
                throw new InvalidOperationException($"SAT>IP RTSP {method} failed with status {response.StatusCode}.");
        }

        private sealed record RtspResponse(int StatusCode, Dictionary<string, string> Headers);
    }
