namespace Thaliak.Service.Poller.Polling.TraditionalChinese;

internal class TraditionalChineseMaintenancePollJob : ScheduledPollJob<TraditionalChineseMaintenanceService>
{
    private readonly PollingScheduleService _scheduleService;

    public TraditionalChineseMaintenancePollJob(TraditionalChineseMaintenanceService poller,
        PollingScheduleService scheduleService) : base(poller)
    {
        _scheduleService = scheduleService;
    }

    protected override DateTime GetNextExecutionTime()
    {
        return _scheduleService.GetNextTraditionalChineseMaintenancePoll(DateTime.UtcNow);
    }
}
