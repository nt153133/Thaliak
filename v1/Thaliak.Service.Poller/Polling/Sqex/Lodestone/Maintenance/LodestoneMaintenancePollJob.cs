namespace Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;

internal class LodestoneMaintenancePollJob : ScheduledPollJob<LodestoneMaintenanceService>
{
    private readonly PollingScheduleService _scheduleService;

    public LodestoneMaintenancePollJob(LodestoneMaintenanceService poller, PollingScheduleService scheduleService) :
        base(poller)
    {
        _scheduleService = scheduleService;
    }

    protected override DateTime GetNextExecutionTime()
    {
        return _scheduleService.GetNextLodestoneMaintenancePoll(DateTime.UtcNow);
    }
}
