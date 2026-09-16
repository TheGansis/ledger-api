using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

/// <summary>OwnerId — только для администратора (открыть счёт клиенту); обычный пользователь открывает себе.</summary>
public sealed record OpenAccountRequest(string OwnerName, string Currency, string? OwnerId = null);

public sealed record AccountDto(Guid Id, string OwnerId, string OwnerName, string Currency, decimal Balance, string Status, DateTimeOffset CreatedAt)
{
    public static AccountDto From(Account a) =>
        new(a.Id, a.OwnerId, a.OwnerName, a.Currency, a.Balance, a.Status.ToString(), a.CreatedAt);
}

public sealed record LedgerEntryDto(Guid Id, Guid TransactionId, decimal Amount, decimal BalanceAfter, DateTimeOffset CreatedAt);
