using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Polling.Sqex;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Polling;

public sealed class SqexAccountProviderTests
{
    [Fact]
    public async Task GetRequired_WithBothRoles_ReturnsRoutineAccount()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.Accounts.AddRange(
            CreateAccount(XivAccountPurpose.Expansion, "full"),
            CreateAccount(XivAccountPurpose.Routine, "trial"));
        await db.SaveChangesAsync();
        var provider = new SqexAccountProvider(db);

        var account = provider.GetRequired(XivAccountPurpose.Routine);

        Assert.Equal("trial", account.Username);
        Assert.Equal(XivAccountPurpose.Routine, account.Purpose);
    }

    private static XivAccount CreateAccount(XivAccountPurpose purpose, string username)
    {
        return new XivAccount
        {
            Purpose = purpose,
            Username = username,
            Password = "secret",
            ApplicableRepositories = []
        };
    }

    private static ThaliakContext CreateContext()
    {
        var path = Path.Combine(Path.GetTempPath(), "thaliak-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new ThaliakContext(
            new DbContextOptionsBuilder<ThaliakContext>()
                .UseSqlite($"Data Source={path}")
                .UseSnakeCaseNamingConvention()
                .Options);
    }
}
