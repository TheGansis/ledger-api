using Ledger.Domain.Common;

namespace Ledger.Domain.Accounts;

/// <summary>
/// Счёт. Баланс хранится денормализованно (Balance) и одновременно восстанавливается
/// как сумма проводок LedgerEntry — это позволяет сверять состояние (reconciliation).
/// Все изменения баланса идут только через Credit/Debit, которые проверяют инварианты.
/// </summary>
public sealed class Account
{
    private Account() { } // EF Core

    private Account(Guid id, string ownerName, string currency, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerName = ownerName;
        Currency = currency;
        Balance = 0m;
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string OwnerName { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal Balance { get; private set; }
    public AccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Токен оптимистичной блокировки (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static Account Open(string ownerName, string currency, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ownerName))
            throw new DomainException("owner.required", "Owner name is required.");
        var money = Money.Of(0m, currency); // валидирует код валюты
        return new Account(Guid.NewGuid(), ownerName.Trim(), money.Currency, now);
    }

    public Money BalanceMoney => new(Balance, Currency);

    /// <summary>Проверка без изменения состояния — чтобы отказ по одному счёту не оставил другой мутированным.</summary>
    public void EnsureCanCredit(Money amount)
    {
        EnsureActive();
        EnsurePositive(amount);
        _ = BalanceMoney.Add(amount); // проверка валюты
    }

    public void EnsureCanDebit(Money amount)
    {
        EnsureActive();
        EnsurePositive(amount);
        if (BalanceMoney.Subtract(amount).Amount < 0)
            throw new DomainException("funds.insufficient",
                $"Insufficient funds: balance {BalanceMoney}, requested {amount}.");
    }

    public void Credit(Money amount)
    {
        EnsureCanCredit(amount);
        Balance = BalanceMoney.Add(amount).Amount;
    }

    public void Debit(Money amount)
    {
        EnsureCanDebit(amount);
        Balance = BalanceMoney.Subtract(amount).Amount;
    }

    public void Freeze()
    {
        if (Status == AccountStatus.Closed)
            throw new DomainException("account.closed", "Closed account cannot be frozen.");
        Status = AccountStatus.Frozen;
    }

    public void Unfreeze()
    {
        if (Status != AccountStatus.Frozen)
            throw new DomainException("account.not_frozen", "Account is not frozen.");
        Status = AccountStatus.Active;
    }

    public void Close()
    {
        if (Balance != 0m)
            throw new DomainException("account.nonzero_balance", "Account with non-zero balance cannot be closed.");
        Status = AccountStatus.Closed;
    }

    private void EnsureActive()
    {
        if (Status != AccountStatus.Active)
            throw new DomainException("account.inactive", $"Account {Id} is {Status}.");
    }

    private void EnsurePositive(Money amount)
    {
        if (!amount.IsPositive)
            throw new DomainException("amount.not_positive", "Amount must be positive.");
    }
}
