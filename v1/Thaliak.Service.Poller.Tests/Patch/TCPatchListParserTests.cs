using Thaliak.Service.Poller.Patch;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Patch;

public class TCPatchListParserTests
{
    [Fact]
    public void Parse_WithLiveStyleRows_SkipsSeparatorsAndParsesEntries()
    {
        const string list = """
                            63754626	55010946524	75	12	2026.05.15.0000.0000	0	x	c2b49d96	https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex0/2026-05-15-0001-0000.patch

                            --
                            17942043	9439643652	28	4	2026.05.15.0000.0000	5	x	c800d7ff	https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex5/2026-05-15-0001-0000.patch
                            """;

        var entries = TCPatchListParser.Parse(list);

        Assert.Equal(2, entries.Length);
        Assert.Equal("2026.05.15.0000.0000", entries[0].VersionId);
        Assert.Equal("0", entries[0].HashType);
        Assert.Equal(["c2b49d96"], entries[0].Hashes);
        Assert.Equal("https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex0/2026-05-15-0001-0000.patch", entries[0].Url);
        Assert.Equal("5", entries[1].HashType);
        Assert.Equal(["c800d7ff"], entries[1].Hashes);
    }
}
