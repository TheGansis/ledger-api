using Ledger.Application.Common;
using Ledger.Domain.Transactions;

namespace Ledger.Application.Abstractions;

public interface ITransactionRepository
{
    Task<Transaction?> FindAsync(Guid id, CancellationToken ct);
    Task<Transaction?> FindByIdempotencyKeyAsync(string key, CancellationToken ct);
    void Add(Transaction transaction);

    Task<Page<LedgerEntry>> GetEntriesAsync(Guid accountId, string? cursor, int limit, CancellationToken ct);
}
