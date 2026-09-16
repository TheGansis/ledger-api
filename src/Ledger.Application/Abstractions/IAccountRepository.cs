using Ledger.Domain.Accounts;

namespace Ledger.Application.Abstractions;

public interface IAccountRepository
{
    Task<Account?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Читает счета с блокировкой строк (SELECT … FOR UPDATE) в детерминированном порядке id.
    /// Порядок одинаков для всех конкурентных транзакций ⇒ дедлоки невозможны.
    /// Должен вызываться внутри открытой транзакции IUnitOfWork.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Account>> GetForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    void Add(Account account);
}
