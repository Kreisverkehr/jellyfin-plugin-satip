using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;

public sealed class RtspSession : IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly ILogger<RtspSession>? _logger;
    private readonly TcpClient _tcpClient;
    private NetworkStream? _controlStream;
    private readonly RtpPortLease _portLease;
    private string? _sessionId;
    private TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(30);
    private CancellationTokenSource? _keepAliveCancellation;
    private Task? _keepAliveTask;
    private CancellationTokenSource? _rtpMonitorCancellation;
    private Task? _rtpMonitorTask;
    private long _receivedRtpPackets;
    private long _receivedRtpBytes;
    private long _lastRtpPacketTimestamp;
    private int _lastRtpSequence = -1;
    private int _firstRtpPacketLogged;
    private int _cseq;

    private NetworkStream ControlStream => _controlStream ?? throw new InvalidOperationException("The RTSP connection has not been opened.");

    private RtspSession(Uri uri, ILogger<RtspSession>? logger, TcpClient tcpClient, RtpPortLease portLease)
    {
        _uri = uri;
        _logger = logger;
        _tcpClient = tcpClient;
        _portLease = portLease;
    }

    public static async Task<RtspSession> OpenAsync(Uri uri, IRtpPortAllocator portAllocator, ILogger<RtspSession>? logger = null, CancellationToken cancellationToken = default)
    {
        logger?.LogEnterMethod();
        var tcpClient = new TcpClient(AddressFamily.InterNetwork);
        RtpPortLease? portLease = null;
        RtspSession? session = null;

        try
        {
            portLease = portAllocator.Allocate();
            session = new RtspSession(uri, logger, tcpClient, portLease);
            await tcpClient.ConnectAsync(uri.DnsSafeHost, uri.Port, cancellationToken).ConfigureAwait(false);
            session._controlStream = tcpClient.GetStream();
            var options = await session.SendRequestAsync("OPTIONS", null, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(options, "OPTIONS");

            var rtpEndPoint = (IPEndPoint)portLease.RtpClient.Client.LocalEndPoint!;
            var rtcpEndPoint = (IPEndPoint)portLease.RtcpClient.Client.LocalEndPoint!;
            logger?.LogRtpPortsBound(rtpEndPoint, rtcpEndPoint);
            var setup = await session.SendRequestAsync("SETUP", new Dictionary<string, string>
            {
                ["Transport"] = $"RTP/AVP;unicast;client_port={rtpEndPoint.Port}-{rtcpEndPoint.Port};mode=\"PLAY\"",
            }, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(setup, "SETUP");
            setup.Headers.TryGetValue("Transport", out var transportHeader);
            logger?.LogRtpTransportNegotiated(transportHeader ?? "<missing>");
            if (setup.Headers.TryGetValue("Session", out var sessionHeader))
                session.InitializeSession(sessionHeader);

            var playHeaders = session._sessionId is null
                ? null
                : new Dictionary<string, string> { ["Session"] = session._sessionId };
            var play = await session.SendRequestAsync("PLAY", playHeaders, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(play, "PLAY");
            session.StartRtpMonitor();
            session.StartKeepAlive();
            logger?.LogExitMethod();
            return session;
        }
        catch
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                portLease?.Dispose();
                tcpClient.Dispose();
            }

            throw;
        }
    }

    public async Task<RtpPacket> ReceiveRtpAsync(CancellationToken cancellationToken)
    {
        var result = await _portLease.RtpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (!RtpPacket.TryParse(result.Buffer, out var packet))
            throw new InvalidDataException("The SAT>IP server sent an invalid RTP packet.");

        Interlocked.Increment(ref _receivedRtpPackets);
        Interlocked.Add(ref _receivedRtpBytes, result.Buffer.Length);
        Volatile.Write(ref _lastRtpSequence, packet.SequenceNumber);
        Volatile.Write(ref _lastRtpPacketTimestamp, Stopwatch.GetTimestamp());
        if (Interlocked.CompareExchange(ref _firstRtpPacketLogged, 1, 0) == 0)
            _logger?.LogFirstRtpPacket(result.RemoteEndPoint, result.Buffer.Length, packet.SequenceNumber);

        return packet;
    }

    public async ValueTask DisposeAsync()
    {
        _keepAliveCancellation?.Cancel();
        if (_keepAliveTask is not null)
            await _keepAliveTask.ConfigureAwait(false);
        _keepAliveCancellation?.Dispose();

        _rtpMonitorCancellation?.Cancel();
        if (_rtpMonitorTask is not null)
            await _rtpMonitorTask.ConfigureAwait(false);
        _rtpMonitorCancellation?.Dispose();

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

        _portLease.Dispose();
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

    private void StartRtpMonitor()
    {
        _rtpMonitorCancellation = new CancellationTokenSource();
        _rtpMonitorTask = MonitorRtpAsync(_rtpMonitorCancellation.Token);
    }

    private async Task MonitorRtpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (Interlocked.Read(ref _receivedRtpPackets) == 0)
                _logger?.LogNoRtpPacketsReceived(_uri, 5);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var packets = Interlocked.Read(ref _receivedRtpPackets);
                var bytes = Interlocked.Read(ref _receivedRtpBytes);
                var lastPacketTimestamp = Volatile.Read(ref _lastRtpPacketTimestamp);
                var timeSinceLastPacket = lastPacketTimestamp == 0
                    ? (TimeSpan?)null
                    : Stopwatch.GetElapsedTime(lastPacketTimestamp);
                _logger?.LogRtpReceiveStatus(_uri, packets, bytes, Volatile.Read(ref _lastRtpSequence), timeSinceLastPacket);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
        _logger?.LogEnterMethod();
        _logger?.LogRtspCommand(method);
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
        var requestText = request.ToString();
        _logger?.LogRtspCommandText(requestText);
        var bytes = Encoding.ASCII.GetBytes(requestText);
        await ControlStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

        var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogExitMethod();
        return response;
    }

    private async Task<RtspResponse> ReadResponseAsync(CancellationToken cancellationToken)
    {
        _logger?.LogEnterMethod();
        var statusLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var statusParts = statusLine.Split(' ', 3);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out var statusCode))
            throw new InvalidDataException("The SAT>IP server returned an invalid RTSP status line.");
        _logger?.LogRtspCommandResponse(statusCode);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line.Length == 0)
                break;

            _logger?.LogRtspCommandResponseHeader(line);
            var separator = line.IndexOf(':');
            if (separator > 0)
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        string bodyText = string.Empty;
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
            bodyText = Encoding.ASCII.GetString(bodyBuffer).Trim(' ', '\0');
            _logger?.LogRtspCommandResponseBody(bodyText);
        }

        _logger?.LogExitMethod();
        return new RtspResponse(statusCode, headers, bodyText);
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

    private static void EnsureSuccess(RtspResponse response, string method)
    {
        if (response.StatusCode is < 200 or >= 300)
            throw new InvalidOperationException($"SAT>IP RTSP {method} failed with status {response.StatusCode}.");
    }

    private sealed record RtspResponse(int StatusCode, Dictionary<string, string> Headers, string BodyText);
}
