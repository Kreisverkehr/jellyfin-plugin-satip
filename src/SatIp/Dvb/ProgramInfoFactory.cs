using System.Runtime.CompilerServices;
using System.Text;
using Kreisverkehr.DvbMetadataDecoder;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;

public interface IProgramInfoFactory
{
    IAsyncEnumerable<ProgramInfo> GetProgramInfoFromEit(IEventInformationTable eit, CancellationToken cancellationToken = default);
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

    public async IAsyncEnumerable<ProgramInfo> GetProgramInfoFromEit(IEventInformationTable eit, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in eit.Events)
        {
            if(item.StartTime is null)
                continue;

            byte[] id = [
                (byte)(eit.OriginalNetworkId >> 8),
                (byte)eit.OriginalNetworkId,
                (byte)(eit.TransportStreamId >> 8),
                (byte)eit.TransportStreamId,
                (byte)(eit.ServiceId >> 8),
                (byte)eit.ServiceId,
                (byte)(item.EventId >> 8),
                (byte)item.EventId,
                (byte)(item.StartTime.Value.Ticks >> 56),
                (byte)(item.StartTime.Value.Ticks >> 48),
                (byte)(item.StartTime.Value.Ticks >> 40),
                (byte)(item.StartTime.Value.Ticks >> 32),
                (byte)(item.StartTime.Value.Ticks >> 24),
                (byte)(item.StartTime.Value.Ticks >> 16),
                (byte)(item.StartTime.Value.Ticks >> 8),
                (byte)item.StartTime.Value.Ticks
            ];
            
            var info = new ProgramInfo(_loggerFactory.CreateLogger<ProgramInfo>())
            {
                Id = new Guid(id).ToString()
            };

            if (item.Descriptors.OfType<IShortEventDescriptor>()?.Any() ?? false)
            {
                var shortDescription = item.Descriptors.OfType<IShortEventDescriptor>().First();
                info.Name = shortDescription.Name;
                info.ShortOverview = shortDescription.Text;
            }

            if (item.Descriptors.OfType<IExtendedEventDescriptor>().Any())
            {
                StringBuilder sb = new StringBuilder();
                var extDescriptors = item.Descriptors.OfType<IExtendedEventDescriptor>();
                foreach (var extDEsc in extDescriptors.Where(d => !string.IsNullOrWhiteSpace(d.Text)))
                {
                    sb.Append(extDEsc.Text);
                }

                if (sb.Length > 0 && extDescriptors.Any(d => d.Items is not null))
                {
                    sb.AppendLine();

                    foreach (var descItem in extDescriptors
                        .Where(d => d.Items is not null)
                        .SelectMany(d => d.Items)
                        .Where(i => !string.IsNullOrEmpty(i.Text)))
                    {
                        if (!string.IsNullOrWhiteSpace(descItem.Description))
                            sb.AppendLine($"{descItem.Description}: {descItem.Text}");
                        else
                            sb.Append(descItem.Text);
                    }
                }

                info.Overview = sb.ToString();
            }

            try
            {
                info.StartDate = item.StartTime ?? DateTime.MinValue;
                info.EndDate = info.StartDate + item.Duration;
            }
            // If these fail, we just don't set them
            catch (Exception exception)
            {
                _logger?.LogInvalidProgramTime(exception, eit.ServiceId, item.EventId);
            }

            info.Genres ??= [];
            foreach (var contentType in item.Descriptors.OfType<IContentDescriptor>().SelectMany(d => d.ContentTypes))
            {
                info.IsMovie |= contentType.Level1Type == 0x1;
                info.IsNews |= contentType.Level1Type == 0x2;
                info.IsSports |= contentType.Level1Type == 0x4;
                info.IsKids |= contentType.Level1Type == 0x5;
                info.IsEducational |= contentType.Level1Type == 0x9;
                // info.Genres.Add(contentType.Level2TypeString);
            }

            yield return info;
        }
    }
}