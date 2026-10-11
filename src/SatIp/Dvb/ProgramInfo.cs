using System.Diagnostics;
using System.Text;
using Cinegy.TsDecoder.Descriptors;
using Cinegy.TsDecoder.Tables;
using Jellyfin.Database.Implementations.Entities.Libraries;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

[DebuggerDisplay("[{Id}] {Name} {StartDate}")]
public class ProgramInfo : MediaBrowser.Controller.LiveTv.ProgramInfo, IEquatable<ProgramInfo>
{
    public ProgramInfo(EventInformationItem eit, EventInformationTable eventInformationTable)
    {
        Id = $"{eit.EventId:x4}-{eventInformationTable.ServiceId:x4}-{eit.StartTime:x10}";
        if (eit.Descriptors.OfType<ShortEventDescriptor>()?.Any() ?? false)
        {
            var shortDescription = eit.Descriptors.OfType<ShortEventDescriptor>().First();
            Name = shortDescription.EventNameChar?.Value;
            ShortOverview = shortDescription.TextChar?.Value;
        }

        if (eit.Descriptors.OfType<ExtendedEventDescriptor>().Any())
        {
            StringBuilder sb = new StringBuilder();
            var extDescriptors = eit.Descriptors.OfType<ExtendedEventDescriptor>();
            foreach (var extDEsc in extDescriptors.Where(d => d.TextChar is not null))
            {
                sb.Append(extDEsc.TextChar.Value);
            }

            if (sb.Length > 0 && extDescriptors.Any(d => d.Items is not null))
            {
                sb.AppendLine();

                foreach (var descItem in extDescriptors
                    .Where(d => d.Items is not null)
                    .SelectMany(d => d.Items)
                    .Where(i => i.ItemChar is not null))
                {
                    if (descItem.ItemDescriptionChar is not null)
                        sb.AppendLine($"{descItem.ItemDescriptionChar.Value}: {descItem.ItemChar.Value}");
                    else
                        sb.Append(descItem.ItemChar.Value);
                }
            }
            
            Overview = sb.ToString();
        }

        try
        {
            StartDate = eit.StartDateTime ?? DateTime.MinValue;
            EndDate = StartDate + eit.DurationTimeSpan;
        }
        // If these fail, we just don't set them
        catch (Exception) { }

        Genres ??= [];
        foreach (var contentType in eit.Descriptors.OfType<ContentDescriptor>().SelectMany(d => d.ContentTypes))
        {
            IsMovie |= contentType.Level1Type == 0x1;
            IsNews |= contentType.Level1Type == 0x2;
            IsSports |= contentType.Level1Type == 0x4;
            IsKids |= contentType.Level1Type == 0x5;
            IsEducational |= contentType.Level1Type == 0x9;
            Genres.Add(contentType.Level2TypeString);
        }
    }

    public bool Equals(ProgramInfo? other) 
        => Id == other?.Id;

    public override bool Equals(object? obj) 
        => obj is ProgramInfo prog && prog.Equals(this);

    public override int GetHashCode()
        => Id?.GetHashCode() ?? 0;
}