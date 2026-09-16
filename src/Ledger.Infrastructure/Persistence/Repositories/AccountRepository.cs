using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(LedgerDbContext db) : IAccountRepository
{
    public Task<Account?> FindAsync(Guid id, CancellationToken ct) =>
        db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, Account>> GetForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        // Пессимистичная блокировка строк. ORDER BY id — все транзакции берут блокировки в одном
        // порядке (A→B и B→A оба сначала возьмут меньший id), поэтому взаимной блокировки не будет.
        // Вторая транзакция просто ждёт коммита первой и читает уже обновлённый баланс.
        // xmin — системный столбец, в SELECT * не входит: его нужно запрашивать явно, иначе
        // EF не сможет заполнить concurrency-token и упадёт при материализации.
        var sorted = ids.Distinct().OrderBy(x => x).ToArray();
        var rows = await db.Accounts
            .FromSqlInterpolated($"SELECT *, xmin FROM ledger.accounts WHERE id = ANY({sorted}) ORDER BY id FOR UPDATE")
            .ToListAsync(ct);
        return rows.ToDictionary(a => a.Id);
    }

    public void Add(Account account) => db.Accounts.Add(account);
}
