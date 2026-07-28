using Microsoft.EntityFrameworkCore;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Exceptions;

namespace Thaliak.Service.Poller.Polling.Sqex;

public sealed class SqexAccountProvider(ThaliakContext db)
{
    public XivAccount GetRequired(XivAccountPurpose purpose)
    {
        var account = db.Accounts.SingleOrDefault(candidate => candidate.Purpose == purpose);
        if (account is null) {
            throw new NoValidAccountException();
        }

        Log.Information("Using {AccountPurpose} account {AccountId}", purpose, account.Id);
        return account;
    }

    public Task<XivAccount?> GetOptionalAsync(
        XivAccountPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        return db.Accounts.SingleOrDefaultAsync(
            candidate => candidate.Purpose == purpose,
            cancellationToken);
    }
}
