using Ledger.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence;

public sealed class UnitOfWork(LedgerDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        // Retry-стратегия Npgsql (EnableRetryOnFailure) требует оборачивать транзакцию в ExecutionStrategy —
        // иначе при переподключении будет повтор половины работы вне транзакции.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
