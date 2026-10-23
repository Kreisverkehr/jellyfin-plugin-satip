using System.Runtime.CompilerServices;
using System.Text;
using Cinegy.TsDecoder.Descriptors;
using Cinegy.TsDecoder.Tables;
using Cinegy.TsDecoder.TransportStream;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

public interface IProgramInfoFactory
{
    IAsyncEnumerable<ProgramInfo> GetProgramInfoFromEit(EventInformationTable eit, CancellationToken cancellationToken = default);
}

public class ProgramInfoFactory : IProgramInfoFactory
{
    private readonly ILogger<ProgramInfoFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ProgramInfoFactory(ILogger<ProgramInfoFactory> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async IAsyncEnumerable<ProgramInfo> GetProgramInfoFromEit(EventInformationTable eit, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in eit.Items)
        {
            var info = new ProgramInfo(_loggerFactory.CreateLogger<ProgramInfo>())
            {
                Id = $"{item.EventId:x4}-{eit.ServiceId:x4}-{item.StartTime:x10}"
            };

            if (item.Descriptors.OfType<ShortEventDescriptor>()?.Any() ?? false)
            {
                var shortDescription = item.Descriptors.OfType<ShortEventDescriptor>().First();
                info.Name = shortDescription.EventNameChar?.Value;
                info.ShortOverview = shortDescription.TextChar?.Value;
            }

            if (item.Descriptors.OfType<ExtendedEventDescriptor>().Any())
            {
                StringBuilder sb = new StringBuilder();
                var extDescriptors = item.Descriptors.OfType<ExtendedEventDescriptor>();
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

                info.Overview = sb.ToString();
            }

            try
            {
                info.StartDate = item.StartDateTime ?? DateTime.MinValue;
                info.EndDate = info.StartDate + item.DurationTimeSpan;
            }
            // If these fail, we just don't set them
            catch (Exception exception)
            {
                _logger?.LogInvalidProgramTime(exception, eit.ServiceId, item.EventId);
            }

            info.Genres ??= [];
            foreach (var contentType in item.Descriptors.OfType<ContentDescriptor>().SelectMany(d => d.ContentTypes))
            {
                info.IsMovie |= contentType.Level1Type == 0x1;
                info.IsNews |= contentType.Level1Type == 0x2;
                info.IsSports |= contentType.Level1Type == 0x4;
                info.IsKids |= contentType.Level1Type == 0x5;
                info.IsEducational |= contentType.Level1Type == 0x9;
                info.Genres.Add(contentType.Level2TypeString);
            }

            yield return info;
        }
    }
}