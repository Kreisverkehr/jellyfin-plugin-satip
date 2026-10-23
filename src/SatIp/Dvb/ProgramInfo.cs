using System.Diagnostics;
using System.Text;
using Cinegy.TsDecoder.Descriptors;
using Cinegy.TsDecoder.Tables;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Jellyfin.Database.Implementations.Entities.Libraries;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

[DebuggerDisplay("[{Id}] {Name} {StartDate}")]
public class ProgramInfo : MediaBrowser.Controller.LiveTv.ProgramInfo, IEquatable<ProgramInfo>
{
    private readonly ILogger<ProgramInfo>? _logger;

    public ProgramInfo(ILogger<ProgramInfo>? logger = null)
    {
        _logger = logger;
    }

    public bool Equals(ProgramInfo? other) 
        => Id == other?.Id;

    public override bool Equals(object? obj) 
        => obj is ProgramInfo prog && prog.Equals(this);

    public override int GetHashCode()
        => Id?.GetHashCode() ?? 0;
}