using Ledger.Domain.Accounts;
using Ledger.Domain.Common;

namespace Ledger.Domain.Tests;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_normalizes_currency_and_starts_with_zero_balance()
    {
        var a = Account.Open("  Alice ", "rub", Now);
        Assert.Equal("Alice", a.OwnerName);
        Assert.Equal("RUB", a.Currency);
        Assert.Equal(0m, a.Balance);
        Assert.Equal(AccountStatus.Active, a.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RU")]
    [InlineData("RUBL")]
    public void Open_rejects_bad_currency(string currency) =>
        Assert.Throws<DomainException>(() => Account.Open("Alice", currency, Now));

    [Fact]
    public void Credit_then_debit_changes_balance()
    {
        var a = Account.Open("Alice", "RUB", Now);
        a.Credit(Money.Of(100m, "RUB"));
        a.Debit(Money.Of(40.5m, "RUB"));
        Assert.Equal(59.5m, a.Balance);
    }

    [Fact]
    public void Debit_over_balance_is_rejected_and_balance_unchanged()
    {
        var a = Account.Open("Alice", "RUB", Now);
        a.Credit(Money.Of(10m, "RUB"));
        var ex = Assert.Throws<DomainException>(() => a.Debit(Money.Of(10.0001m, "RUB")));
        Assert.Equal("funds.insufficient", ex.Code);
        Assert.Equal(10m, a.Balance);
    }

    [Fact]
    public void Currency_mismatch_is_rejected()
    {
        var a = Account.Open("Alice", "RUB", Now);
        var ex = Assert.Throws<DomainException>(() => a.Credit(Money.Of(1m, "USD")));
        Assert.Equal("currency.mismatch", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_amounts_are_rejected(decimal amount)
    {
        var a = Account.Open("Alice", "RUB", Now);
        Assert.Throws<DomainException>(() => a.Credit(Money.Of(amount, "RUB")));
        Assert.Throws<DomainException>(() => a.Debit(Money.Of(amount, "RUB")));
    }

    [Fact]
    public void Frozen_account_rejects_operations_but_can_be_unfrozen()
    {
        var a = Account.Open("Alice", "RUB", Now);
        a.Freeze();
        Assert.Throws<DomainException>(() => a.Credit(Money.Of(1m, "RUB")));
        a.Unfreeze();
        a.Credit(Money.Of(1m, "RUB"));
        Assert.Equal(1m, a.Balance);
    }

    [Fact]
    public void Close_requires_zero_balance()
    {
        var a = Account.Open("Alice", "RUB", Now);
        a.Credit(Money.Of(1m, "RUB"));
        Assert.Throws<DomainException>(a.Close);
        a.Debit(Money.Of(1m, "RUB"));
        a.Close();
        Assert.Equal(AccountStatus.Closed, a.Status);
        Assert.Throws<DomainException>(a.Freeze);
    }

    [Fact]
    public void Money_rejects_more_than_four_decimals() =>
        Assert.Throws<DomainException>(() => Money.Of(1.00001m, "RUB"));
}
