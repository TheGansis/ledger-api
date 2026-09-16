using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed record OpenAccountRequest(string OwnerName, string Currency);

public sealed record AccountDto(Guid Id, string OwnerName, string Currency, decimal Balance, string Status, DateTimeOffset CreatedAt)
{
    public static AccountDto From(Account a) =>
        new(a.Id, a.OwnerName, a.Currency, a.Balance, a.Status.ToString(), a.CreatedAt);
}

public sealed record LedgerEntryDto(Guid Id, Guid TransactionId, decimal Amount, decimal BalanceAfter, DateTimeOffset CreatedAt);
