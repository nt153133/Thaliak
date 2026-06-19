namespace Thaliak.Service.Poller.Polling.TraditionalChinese;

internal class TraditionalChinesePatchListPollJob : ScheduledPollJob<TraditionalChinesePollerService>
{
    private readonly PollingScheduleService _scheduleService;

    public TraditionalChinesePatchListPollJob(TraditionalChinesePollerService poller,
        PollingScheduleService scheduleService) : base(poller)
    {
        _scheduleService = scheduleService;
    }

    protected override DateTime GetNextExecutionTime()
    {
        return _scheduleService.GetNextTraditionalChinesePatchPoll(DateTime.UtcNow);
    }
}
