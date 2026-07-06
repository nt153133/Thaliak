using Microsoft.Extensions.Configuration;
using Thaliak.Service.Poller.Polling;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Polling;

public class PollingConfigurationTests
{
    [Fact]
    public void ShouldRegisterPolling_WhenUnset_ReturnsTrue()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.True(PollingConfiguration.ShouldRegisterPolling(configuration));
    }

    [Fact]
    public void ShouldRegisterPolling_WhenDisabled_ReturnsFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PollingConfiguration.EnabledKey] = "false"
            })
            .Build();

        Assert.False(PollingConfiguration.ShouldRegisterPolling(configuration));
    }

    [Fact]
    public void ShouldRegisterKoreaChecks_WhenDisableFlagTrue_ReturnsFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PollingConfiguration.DisableKoreaChecksKey] = "true"
            })
            .Build();

        var shouldRegister = PollingConfiguration.ShouldRegisterKoreaChecks(configuration);

        Assert.False(shouldRegister);
    }
}
