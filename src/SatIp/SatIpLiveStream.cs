using System.Threading.Channels;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using System.Text.Json;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

internal sealed class SatIpLiveStream : ILiveStream
{
    private const int PAYLOAD_BUFFER_COUNT = 16;
    private readonly IRtspSessionFactory _rtspSessionFactory;
    private readonly IServerApplicationHost _applicationHost;
    private readonly Uri _streamUri;
    private readonly Channel<byte[]> _payloads = Channel.CreateUnbounded<byte[]>(new()
    {
        SingleReader = true,
        SingleWriter = true
    });
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Stream _stream;
    private readonly Lock _closeLock = new();
    private RtspSession? _session;
    private Task? _receiveTask;
    private Task? _closeTask;
    private bool _closed;

    public SatIpLiveStream(IRtspSessionFactory rtspSessionFactory, IServerApplicationHost applicationHost, MediaSourceInfo mediaSource, string tunerHostId, string originalStreamId)
    {
        if (!Uri.TryCreate(mediaSource.Path, UriKind.Absolute, out var streamUri))
            throw new ArgumentException("The media source does not contain a valid absolute URI.", nameof(mediaSource));

        _streamUri = streamUri;
        _rtspSessionFactory = rtspSessionFactory;
        _applicationHost = applicationHost;
        UniqueId = Guid.NewGuid().ToString("N");
        TunerHostId = tunerHostId;
        OriginalStreamId = originalStreamId;
        MediaSource = JsonSerializer.Deserialize<MediaSourceInfo>(JsonSerializer.Serialize(mediaSource))
            ?? throw new InvalidOperationException("The media source could not be copied.");
        MediaSource.Path = $"{_applicationHost.GetApiUrlForLocalAccess().TrimEnd('/')}/LiveTv/LiveStreamFiles/{UniqueId}/stream.ts";
        MediaSource.Protocol = MediaProtocol.Http;
        _stream = new RtpPayloadStream(_payloads.Reader);
    }

    public int ConsumerCount { get; set; }

    public string OriginalStreamId { get; set; }

    public string TunerHostId { get; }

    public bool EnableStreamSharing => true;

    public MediaSourceInfo MediaSource { get; set; }

    public string UniqueId { get; }

    public async Task Open(CancellationToken openCancellationToken)
    {
        await _lifecycleGate.WaitAsync(openCancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_session is not null)
                return;

            _session = await _rtspSessionFactory.OpenAsync(_streamUri, openCancellationToken).ConfigureAwait(false);
            _receiveTask = ReceivePayloadsAsync(_session, _receiveCancellation.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Stream GetStream() => _stream;

    public Task Close()
    {
        lock (_closeLock)
            return _closeTask ??= CloseCoreAsync();
    }

    public void Dispose() => Close().GetAwaiter().GetResult();

    private async Task ReceivePayloadsAsync(RtspSession session, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var packet = await session.ReceiveRtpAsync(cancellationToken).ConfigureAwait(false);
                await _payloads.Writer.WriteAsync(packet.Payload.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            _payloads.Writer.TryComplete(failure);
        }
    }

    private async Task CloseCoreAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        RtspSession? session;
        Task? receiveTask;
        try
        {
            _closed = true;
            _receiveCancellation.Cancel();
            session = _session;
            receiveTask = _receiveTask;
            _session = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (receiveTask is not null)
            await receiveTask.ConfigureAwait(false);

        try
        {
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _payloads.Writer.TryComplete();
            _receiveCancellation.Dispose();
        }
    }

    private sealed class RtpPayloadStream : Stream
    {
        private readonly ChannelReader<byte[]> _reader;
        private byte[]? _currentPayload;
        private int _currentOffset;
        private bool _disposed;

        public RtpPayloadStream(ChannelReader<byte[]> reader)
        {
            _reader = reader;
        }

        public override bool CanRead => !_disposed;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (buffer.Length == 0)
                return 0;

            while (_currentPayload is null || _currentOffset == _currentPayload.Length)
            {
                _currentPayload = null;
                _currentOffset = 0;
                if (!await _reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    return 0;

                if (_reader.TryRead(out var payload))
                    _currentPayload = payload;
            }

            var bytesToCopy = Math.Min(buffer.Length, _currentPayload.Length - _currentOffset);
            _currentPayload.AsMemory(_currentOffset, bytesToCopy).CopyTo(buffer);
            _currentOffset += bytesToCopy;
            return bytesToCopy;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
