namespace Thaliak.Service.Poller.Webhooks;

public static class PatchRegion
{
    public const string Global = "Global";
    public const string Korea = "Korea";
    public const string China = "China";
    public const string TraditionalChinese = "TC";

    public static string? FromServiceId(int serviceId)
    {
        return serviceId switch
        {
            1 => Global,
            2 => Korea,
            3 => China,
            4 => TraditionalChinese,
            _ => null
        };
    }
}
