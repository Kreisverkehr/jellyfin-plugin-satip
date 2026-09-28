using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class LiveTvService : ILiveTvService
{
    public string Name => throw new NotImplementedException();

    public string HomePageUrl => throw new NotImplementedException();

    public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task CloseLiveStream(string id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<MediaSourceInfo> GetChannelStream(string channelId, string streamId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo program = null)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ResetTuner(string id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task UpdateTimerAsync(TimerInfo updatedTimer, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}