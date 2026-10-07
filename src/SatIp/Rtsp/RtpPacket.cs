using System.Buffers.Binary;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;

public readonly struct RtpPacket
{
    private readonly ReadOnlyMemory<byte> _datagram;
    private readonly int _payloadOffset;
    private readonly int _payloadLength;

    private RtpPacket(
        ReadOnlyMemory<byte> datagram,
        int payloadOffset,
        int payloadLength,
        byte version,
        bool hasPadding,
        bool hasExtension,
        byte csrcCount,
        bool marker,
        byte payloadType,
        ushort sequenceNumber,
        uint timestamp,
        uint ssrc)
    {
        _datagram = datagram;
        _payloadOffset = payloadOffset;
        _payloadLength = payloadLength;
        Version = version;
        HasPadding = hasPadding;
        HasExtension = hasExtension;
        CsrcCount = csrcCount;
        Marker = marker;
        PayloadType = payloadType;
        SequenceNumber = sequenceNumber;
        Timestamp = timestamp;
        Ssrc = ssrc;
    }

    public byte Version { get; }

    public bool HasPadding { get; }

    public bool HasExtension { get; }

    public byte CsrcCount { get; }

    public bool Marker { get; }

    public byte PayloadType { get; }

    public ushort SequenceNumber { get; }

    public uint Timestamp { get; }

    public uint Ssrc { get; }

    public ReadOnlyMemory<byte> Payload => _datagram.Slice(_payloadOffset, _payloadLength);

    public static bool TryParse(ReadOnlyMemory<byte> datagram, out RtpPacket packet)
    {
        packet = default;
        var span = datagram.Span;
        if (span.Length < 12)
            return false;

        // The first 2 bits of the first byte represent the version
        var version = (byte)(span[0] >> 6);
        if (version != 2)
            return false;

        // The second nibble of the first byte contains the CSRC count. Use this to calculate the 
        // start of the actual payload
        var csrcCount = (byte)(span[0] & 0x0f);
        var offset = 12 + csrcCount * 4;
        if (offset > span.Length)
            return false;

        // The fourth bit of the first byte is a flag wether the packet contains extensions or not.
        // These are written after the CSRC Identifiers. Use this to calculate the start of the actual payload
        var hasExtension = (span[0] & 0x10) != 0;
        if (hasExtension)
        {
            if (span.Length - offset < 4)
                return false;

            var extensionLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 2, 2)) * 4;
            if (extensionLength > span.Length - offset - 4)
                return false;

            offset += 4 + extensionLength;
        }
 
        // If the third bit of the first byte is set, the packet contains padding. The last byte 
        // of the packet contains the number of padding bytes. Use this to calculate the end of 
        // the actual payload
        var hasPadding = (span[0] & 0x20) != 0;
        var end = span.Length;
        if (hasPadding)
        {
            var paddingLength = span[^1];
            if (paddingLength == 0 || paddingLength > end - offset)
                return false;

            end -= paddingLength;
        }

        if (offset >= end)
            return false;

        var markerAndPayloadType = span[1];
        packet = new RtpPacket(
            datagram,
            offset,
            end - offset,
            version,
            hasPadding,
            hasExtension,
            csrcCount,
            (markerAndPayloadType & 0x80) != 0,
            (byte)(markerAndPayloadType & 0x7f),
            BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4)));
        return true;
    }
}