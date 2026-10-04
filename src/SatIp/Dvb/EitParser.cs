using System.Text;
using MediaBrowser.Controller.LiveTv;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

internal class ProgramInfoDiscoveredEventArgs : EventArgs
{
    public ProgramInfo ProgramInfo { get; }

    public ProgramInfoDiscoveredEventArgs(ProgramInfo programInfo)
    {
        ProgramInfo = programInfo;
    }
}

internal sealed class EitParser
{
    private static readonly HashSet<int> SharedPids = [0, 16, 17, 18, 20];
    private readonly HashSet<int> _requestedPids;
    private readonly Dictionary<int, SectionAssembler> _assemblers = [];
    private readonly Dictionary<int, int> _programMapPids = [];
    private readonly HashSet<int> _matchingServices = [];
    private readonly Dictionary<(int ServiceId, int EventId, DateTime Start), (string Name, string Overview, DateTime End)> _events = [];

    public EitParser(IEnumerable<int> requestedPids)
    {
        _requestedPids = requestedPids.ToHashSet();
    }

    public int MatchingServiceCount => _matchingServices.Count;

    public event EventHandler<ProgramInfoDiscoveredEventArgs>? ProgramInfoDiscovered;

    public void ProcessTransportPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != 188 || packet[0] != 0x47 || (packet[1] & 0x80) != 0)
            return;

        var payloadStart = (packet[1] & 0x40) != 0;
        var pid = ((packet[1] & 0x1f) << 8) | packet[2];
        var adaptationControl = (packet[3] >> 4) & 0x03;
        if ((adaptationControl & 0x01) == 0)
            return;

        var offset = 4;
        if ((adaptationControl & 0x02) != 0)
        {
            offset += packet[offset] + 1;
            if (offset >= packet.Length)
                return;
        }

        if (!_assemblers.TryGetValue(pid, out var assembler))
        {
            assembler = new SectionAssembler();
            _assemblers.Add(pid, assembler);
        }

        assembler.Process(packet[offset..], payloadStart, section => ProcessSection(pid, section));
    }

    public IEnumerable<ProgramInfo> GetPrograms(string channelId, DateTime startDateUtc, DateTime endDateUtc)
    {
        if (_matchingServices.Count != 1)
            return [];

        var serviceId = _matchingServices.Single();
        return _events
            .Where(entry => entry.Key.ServiceId == serviceId && entry.Key.Start < endDateUtc && entry.Value.End > startDateUtc)
            .Select(entry => new ProgramInfo
            {
                Id = $"{serviceId:x4}-{entry.Key.EventId:x4}-{entry.Key.Start:O}",
                ChannelId = channelId,
                Name = entry.Value.Name,
                Overview = entry.Value.Overview,
                StartDate = entry.Key.Start,
                EndDate = entry.Value.End,
            })
            .OrderBy(program => program.StartDate)
            .ToList();
    }

    private void RaiseProgramInfoDiscovered(int ServiceId, int EventId, DateTime Start, string Name, string Overview, DateTime End)
    {
        if(_matchingServices.Count != 1)
            return;
            
        var requestedServiceId = _matchingServices.Single();
        if(ServiceId != requestedServiceId)
            return;

        var programInfo = new ProgramInfo
        {
            Id = $"{ServiceId:x4}-{EventId:x4}-{Start:O}",
            Name = Name,
            Overview = Overview,
            StartDate = Start,
            EndDate = End,
        };

        OnProgramInfoDiscovered(programInfo);
    }

    private void OnProgramInfoDiscovered(ProgramInfo programInfo)
    {
        ProgramInfoDiscovered?.Invoke(this, new ProgramInfoDiscoveredEventArgs(programInfo));
    }

    private void ProcessSection(int pid, byte[] section)
    {
        if (section.Length < 8 || (section[5] & 0x01) == 0)
            return;

        switch (section[0])
        {
            case 0x00 when pid == 0:
                ProcessProgramAssociation(section);
                break;
            case 0x02 when _programMapPids.ContainsValue(pid):
                ProcessProgramMap(section);
                break;
            case 0x4e or (>= 0x50 and <= 0x5f) when pid == 18:
                ProcessEventInformation(section);
                break;
        }
    }

    private void ProcessProgramAssociation(byte[] section)
    {
        var end = section.Length - 4;
        for (var offset = 8; offset + 4 <= end; offset += 4)
        {
            var serviceId = (section[offset] << 8) | section[offset + 1];
            if (serviceId == 0)
                continue;

            var pid = ((section[offset + 2] & 0x1f) << 8) | section[offset + 3];
            _programMapPids[serviceId] = pid;
        }
    }

    private void ProcessProgramMap(byte[] section)
    {
        if (section.Length < 16)
            return;

        var serviceId = (section[3] << 8) | section[4];
        var programInfoLength = ((section[10] & 0x0f) << 8) | section[11];
        var offset = 12 + programInfoLength;
        var end = section.Length - 4;
        if (offset > end)
            return;

        var elementaryPids = new List<int>();
        while (offset + 5 <= end)
        {
            var elementaryPid = ((section[offset + 1] & 0x1f) << 8) | section[offset + 2];
            var descriptorsLength = ((section[offset + 3] & 0x0f) << 8) | section[offset + 4];
            if (offset + 5 + descriptorsLength > end)
                return;

            elementaryPids.Add(elementaryPid);
            offset += 5 + descriptorsLength;
        }

        var servicePids = _requestedPids.Where(pid => !SharedPids.Contains(pid)).ToHashSet();
        if (elementaryPids.Count > 0 && servicePids.Count > 0 && elementaryPids.All(servicePids.Contains))
            _matchingServices.Add(serviceId);
    }

    private void ProcessEventInformation(byte[] section)
    {
        if (section.Length < 18)
            return;

        var serviceId = (section[3] << 8) | section[4];
        var offset = 14;
        var end = section.Length - 4;
        while (offset + 12 <= end)
        {
            var eventId = (section[offset] << 8) | section[offset + 1];
            if (!TryReadStartTime(section.AsSpan(offset + 2, 5), out var start))
            {
                offset += 12 + (((section[offset + 10] & 0x0f) << 8) | section[offset + 11]);
                continue;
            }

            if (!TryReadDuration(section.AsSpan(offset + 7, 3), out var duration))
            {
                offset += 12 + (((section[offset + 10] & 0x0f) << 8) | section[offset + 11]);
                continue;
            }

            var descriptorsLength = ((section[offset + 10] & 0x0f) << 8) | section[offset + 11];
            var descriptorStart = offset + 12;
            var descriptorEnd = descriptorStart + descriptorsLength;
            if (descriptorEnd > end)
                return;

            var (name, overview) = ReadEventText(section.AsSpan(descriptorStart, descriptorsLength));
            _events[(serviceId, eventId, start)] = (name, overview, start + duration);
            RaiseProgramInfoDiscovered(serviceId, eventId, start, name, overview, start + duration);
            offset = descriptorEnd;
        }
    }

    private static (string Name, string Overview) ReadEventText(ReadOnlySpan<byte> descriptors)
    {
        string name = string.Empty;
        string overview = string.Empty;

        for (var offset = 0; offset + 2 <= descriptors.Length;)
        {
            var tag = descriptors[offset];
            var length = descriptors[offset + 1];
            offset += 2;
            if (offset + length > descriptors.Length)
                break;

            if (tag == 0x4d && length >= 5)
            {
                var descriptor = descriptors.Slice(offset, length);
                var nameLength = descriptor[3];
                if (4 + nameLength < descriptor.Length)
                {
                    var textLengthOffset = 4 + nameLength;
                    var textLength = descriptor[textLengthOffset];
                    if (textLengthOffset + 1 + textLength <= descriptor.Length)
                    {
                        name = DecodeDvbText(descriptor.Slice(4, nameLength));
                        overview = DecodeDvbText(descriptor.Slice(textLengthOffset + 1, textLength));
                        if (name.Length > 0)
                            break;
                    }
                }
            }

            offset += length;
        }

        return (name, overview);
    }

    private static string DecodeDvbText(ReadOnlySpan<byte> text)
    {
        if (text.IsEmpty)
            return string.Empty;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            if (text[0] == 0x15)
                return Encoding.UTF8.GetString(text[1..]).TrimEnd('\0', ' ');

            if (text[0] == 0x10 && text.Length >= 3 && text[1] == 0)
                return Encoding.GetEncoding(28590 + text[2]).GetString(text[3..]).TrimEnd('\0', ' ');

            if (text[0] is >= 0x01 and <= 0x0b)
            {
                var isoNumber = text[0] switch
                {
                    <= 0x07 => text[0] + 4,
                    0x09 => 13,
                    0x0a => 14,
                    _ => 15,
                };
                return Encoding.GetEncoding(28590 + isoNumber).GetString(text[1..]).TrimEnd('\0', ' ');
            }

            return Encoding.Latin1.GetString(text).TrimEnd('\0', ' ');
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1.GetString(text).TrimEnd('\0', ' ');
        }
    }

    private static bool TryReadStartTime(ReadOnlySpan<byte> bytes, out DateTime start)
    {
        start = default;
        if (bytes.Length < 5 || bytes[2] == 0xff || bytes[3] == 0xff || bytes[4] == 0xff)
            return false;

        if (!TryReadBcd(bytes[2], out var hours) || !TryReadBcd(bytes[3], out var minutes) || !TryReadBcd(bytes[4], out var seconds)
            || hours > 23 || minutes > 59 || seconds > 59)
            return false;

        var modifiedJulianDate = (bytes[0] << 8) | bytes[1];
        start = new DateTime(1858, 11, 17, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(modifiedJulianDate)
            .AddHours(hours)
            .AddMinutes(minutes)
            .AddSeconds(seconds);
        return true;
    }

    private static bool TryReadDuration(ReadOnlySpan<byte> bytes, out TimeSpan duration)
    {
        duration = default;
        if (bytes.Length < 3 || !TryReadBcd(bytes[0], out var hours) || !TryReadBcd(bytes[1], out var minutes) || !TryReadBcd(bytes[2], out var seconds)
            || minutes > 59 || seconds > 59)
            return false;

        duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return duration > TimeSpan.Zero;
    }

    private static bool TryReadBcd(byte value, out int result)
    {
        var high = value >> 4;
        var low = value & 0x0f;
        result = (high * 10) + low;
        return high <= 9 && low <= 9;
    }

    private sealed class SectionAssembler
    {
        private readonly List<byte> _partialSection = [];

        public void Process(ReadOnlySpan<byte> payload, bool payloadStart, Action<byte[]> onSection)
        {
            if (payloadStart)
            {
                if (payload.IsEmpty)
                    return;

                var pointerLength = payload[0];
                if (pointerLength + 1 > payload.Length)
                    return;

                CompletePartialSection(payload.Slice(1, pointerLength), onSection);
                _partialSection.Clear();
                ProcessNewSections(payload[(pointerLength + 1)..], onSection);
                return;
            }

            CompletePartialSection(payload, onSection);
        }

        private void CompletePartialSection(ReadOnlySpan<byte> continuation, Action<byte[]> onSection)
        {
            if (_partialSection.Count == 0)
                return;

            for (var index = 0; index < continuation.Length; index++)
            {
                _partialSection.Add(continuation[index]);
                if (_partialSection.Count == 3)
                {
                    var sectionLength = 3 + (((_partialSection[1] & 0x0f) << 8) | _partialSection[2]);
                    if (sectionLength > 4096)
                    {
                        _partialSection.Clear();
                        return;
                    }
                }

                if (_partialSection.Count >= 3)
                {
                    var expectedLength = 3 + (((_partialSection[1] & 0x0f) << 8) | _partialSection[2]);
                    if (_partialSection.Count == expectedLength)
                    {
                        onSection(_partialSection.ToArray());
                        _partialSection.Clear();
                        return;
                    }
                }
            }
        }

        private void ProcessNewSections(ReadOnlySpan<byte> payload, Action<byte[]> onSection)
        {
            var offset = 0;
            while (offset < payload.Length && payload[offset] != 0xff)
            {
                if (payload.Length - offset < 3)
                {
                    _partialSection.AddRange(payload[offset..].ToArray());
                    return;
                }

                var sectionLength = 3 + (((payload[offset + 1] & 0x0f) << 8) | payload[offset + 2]);
                if (sectionLength > 4096)
                    return;

                if (offset + sectionLength > payload.Length)
                {
                    _partialSection.AddRange(payload[offset..].ToArray());
                    return;
                }

                onSection(payload.Slice(offset, sectionLength).ToArray());
                offset += sectionLength;
            }
        }
    }
}