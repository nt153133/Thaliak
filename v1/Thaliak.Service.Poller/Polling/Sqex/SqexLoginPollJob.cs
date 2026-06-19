namespace Thaliak.Service.Poller.Polling.Sqex;

internal class SqexLoginPollJob : ScheduledPollJob<SqexPollerService>
{
    private readonly PollingScheduleService _scheduleService;

    public SqexLoginPollJob(SqexPollerService poller, PollingScheduleService scheduleService) : base(poller)
    {
        _scheduleService = scheduleService;
    }

    protected override DateTime GetNextExecutionTime()
    {
        return _scheduleService.GetNextGlobalOrChinaPatchPoll(DateTime.UtcNow);
    }
}
