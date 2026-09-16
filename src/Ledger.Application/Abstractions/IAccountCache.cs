using Ledger.Application.Accounts;

namespace Ledger.Application.Abstractions;

/// <summary>Cache-aside для карточки счёта. Промах или недоступность кэша — не ошибка, идём в БД.</summary>
public interface IAccountCache
{
    Task<AccountDto?> GetAsync(Guid id, CancellationToken ct);
    Task SetAsync(AccountDto account, CancellationToken ct);
    Task InvalidateAsync(IEnumerable<Guid> ids, CancellationToken ct);
}
