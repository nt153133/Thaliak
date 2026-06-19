namespace Thaliak.Service.Poller.Polling.Shanda;

internal class ShandaPatchListPollJob : ScheduledPollJob<ShandaPollerService>
{
    private readonly PollingScheduleService _scheduleService;

    public ShandaPatchListPollJob(ShandaPollerService poller, PollingScheduleService scheduleService) : base(poller)
    {
        _scheduleService = scheduleService;
    }

    protected override DateTime GetNextExecutionTime()
    {
        return _scheduleService.GetNextGlobalOrChinaPatchPoll(DateTime.UtcNow);
    }
}
