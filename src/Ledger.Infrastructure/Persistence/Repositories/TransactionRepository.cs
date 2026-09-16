using Ledger.Application.Abstractions;
using Ledger.Application.Common;
using Ledger.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository(LedgerDbContext db) : ITransactionRepository
{
    public Task<Transaction?> FindAsync(Guid id, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Transaction?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == key, ct);

    public void Add(Transaction transaction) => db.Transactions.Add(transaction);

    public async Task<Page<LedgerEntry>> GetEntriesAsync(Guid accountId, string? cursor, int limit, CancellationToken ct)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(e => e.AccountId == accountId);

        // Курсор = sequence последней выданной проводки; выписка идёт от новых к старым.
        if (cursor is not null && long.TryParse(cursor, out var after))
            query = query.Where(e => e.Sequence < after);

        // Берём на одну больше, чтобы понять, есть ли следующая страница, не делая COUNT(*).
        var rows = await query.OrderByDescending(e => e.Sequence).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new Page<LedgerEntry>(rows, hasMore ? rows[^1].Sequence.ToString() : null);
    }
}
