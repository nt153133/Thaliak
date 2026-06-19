namespace Thaliak.Service.Poller.Polling.Sqex;

internal class SqexFutureScrapeJob : ScheduledPollJob<SqexFutureScraperService>
{
    private readonly PollingScheduleService _scheduleService;

    public SqexFutureScrapeJob(SqexFutureScraperService poller, PollingScheduleService scheduleService) : base(poller)
    {
        _scheduleService = scheduleService;
    }

    protected override DateTime GetNextExecutionTime()
    {
        return _scheduleService.GetNextSqexFutureScrapePoll(DateTime.UtcNow);
    }
}
