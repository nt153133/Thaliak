using Thaliak.Common.Database.Models;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Database;

public class TraditionalChinesePathTests
{
    [Theory]
    [InlineData("https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex0/2026-05-15-0001-0000.patch", 0)]
    [InlineData("https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex5/2026-05-15-0001-0000.patch", 5)]
    public void GetExpansionId_WithTraditionalChineseUrl_ReturnsExpansion(string url, int expectedExpansion)
    {
        var expansion = XivExpansionRepositoryMapping.GetExpansionId(url);

        Assert.Equal(expectedExpansion, expansion);
    }

    [Fact]
    public void LocalStoragePath_WithTraditionalChineseUrl_ReturnsStablePath()
    {
        var patch = new XivPatch
        {
            RemoteOriginPath = "https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex5/2026-05-15-0001-0000.patch"
        };

        Assert.Equal("mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex5/2026-05-15-0001-0000.patch",
            patch.LocalStoragePath);
    }
}
