using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1000,
        EventName = "SAT>IP EPG scan started",
        Level = LogLevel.Information,
        Message = "Scan for updated program info on channel {channelId} started"
        )]
    internal static partial void LogStartEpgScan(this ILogger logger, string channelId);

    [LoggerMessage(
        EventId = 1010,
        EventName = "SAT>IP EPG scan finished",
        Level = LogLevel.Information,
        Message = "Scan finished on channel {channelId}. Got {numPrograms} unique programs."
        )]
    internal static partial void LogEndEpgScan(this ILogger logger, string channelId, int numPrograms);

    [LoggerMessage(
        EventId = 1020,
        EventName = "SAT>IP EPG media source missing",
        Level = LogLevel.Warning,
        Message = "No media source found for channel {ChannelId}"
        )]
    internal static partial void LogNoMediaSource(this ILogger logger, string channelId);

    [LoggerMessage(
        EventId = 1030,
        EventName = "SAT>IP EPG media source invalid",
        Level = LogLevel.Warning,
        Message = "Channel {ChannelId} does not have a valid RTSP media source"
        )]
    internal static partial void LogInvalidRtspMediaSource(this ILogger logger, string channelId);

    [LoggerMessage(
        EventId = 1040,
        EventName = "SAT>IP EPG scan running",
        Level = LogLevel.Information,
        Message = "Scanning programs for channel {ChannelId}"
        )]
    internal static partial void LogScanningPrograms(this ILogger logger, string channelId);

    [LoggerMessage(
        EventId = 1041,
        EventName = "SAT>IP EPG program discovered",
        Level = LogLevel.Debug,
        Message = "Discovered program {ProgramId}: {ProgramName} ({ProgramStart} - {ProgramEnd})"
        )]
    internal static partial void LogDiscoveredProgram(this ILogger logger, string? programId, string? programName, DateTime programStart, DateTime programEnd);

    [LoggerMessage(
        EventId = 1042,
        EventName = "SAT>IP service ID discovered",
        Level = LogLevel.Debug,
        Message = "Found ServiceId {serviceId} for PMT-PID {pmtPid}"
        )]
    internal static partial void LogFoundServiceId(this ILogger logger, ushort serviceId, ushort pmtPid);

    [LoggerMessage(
        EventId = 1050,
        EventName = "SAT>IP stream URI has invalid PIDs",
        Level = LogLevel.Warning,
        Message = "The SAT>IP URI does not contain a valid pids parameter. ({StreamUri})"
        )]
    internal static partial void LogInvalidPidsParameter(this ILogger logger, Uri streamUri);

    [LoggerMessage(
        EventId = 1060,
        EventName = "SAT>IP program time invalid",
        Level = LogLevel.Warning,
        Message = "Unable to determine program time for service {ServiceId}, event {EventId}"
        )]
    internal static partial void LogInvalidProgramTime(this ILogger logger, Exception exception, ushort serviceId, ushort eventId);

    [LoggerMessage(
        EventId = 2010,
        EventName = "Jellyfin Live TV configuration store missing",
        Level = LogLevel.Warning,
        Message = "Jellyfin Live TV configuration store was not found"
        )]
    internal static partial void LogMissingLiveTvConfigurationStore(this ILogger logger);

    [LoggerMessage(
        EventId = 2020,
        EventName = "SAT>IP tuner hosts not configured",
        Level = LogLevel.Information,
        Message = "No SAT>IP tuner hosts are configured"
        )]
    internal static partial void LogNoTunerHostsConfigured(this ILogger logger);

    [LoggerMessage(
        EventId = 2030,
        EventName = "SAT>IP server not known",
        Level = LogLevel.Information,
        Message = "SAT>IP server {DeviceId} is not known; searching for its UDN"
        )]
    internal static partial void LogUnknownSatIpServer(this ILogger logger, string deviceId);

    [LoggerMessage(
        EventId = 2040,
        EventName = "SAT>IP device M3U URL missing",
        Level = LogLevel.Warning,
        Message = "Device {Device} has no M3U URL"
        )]
    internal static partial void LogMissingM3uUrl(this ILogger logger, string device);

    [LoggerMessage(
        EventId = 2050,
        EventName = "SAT>IP M3U playlist loading",
        Level = LogLevel.Information,
        Message = "Loading M3U playlist from {M3UUrl} for device {Device}"
        )]
    internal static partial void LogLoadingM3uPlaylist(this ILogger logger, string m3uUrl, string device);

    [LoggerMessage(
        EventId = 2060,
        EventName = "SAT>IP server discovery failed",
        Level = LogLevel.Warning,
        Message = "SAT>IP server {DeviceId} was not found after discovery"
        )]
    internal static partial void LogSatIpServerDiscoveryFailed(this ILogger logger, string deviceId);

    [LoggerMessage(
        EventId = 2070,
        EventName = "SAT>IP M3U playlist load failed",
        Level = LogLevel.Warning,
        Message = "Failed to load M3U playlist {M3UUrl} for device {Device}"
        )]
    internal static partial void LogM3uPlaylistLoadFailed(this ILogger logger, Exception exception, string m3uUrl, string device);

    [LoggerMessage(
        EventId = 3010,
        EventName = "SAT>IP RTSP TEARDOWN requested",
        Level = LogLevel.Debug,
        Message = "Sending RTSP TEARDOWN to {StreamUri}"
        )]
    internal static partial void LogRtspTeardownRequested(this ILogger logger, Uri streamUri);

    [LoggerMessage(
        EventId = 3020,
        EventName = "SAT>IP RTSP TEARDOWN failed",
        Level = LogLevel.Debug,
        Message = "RTSP TEARDOWN to {StreamUri} failed"
        )]
    internal static partial void LogRtspTeardownFailed(this ILogger logger, Exception exception, Uri streamUri);
}