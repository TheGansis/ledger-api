using Ledger.Domain.Accounts;
using Ledger.Domain.Common;
using Ledger.Domain.Transactions;

namespace Ledger.Domain.Tests;

public class TransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Account Funded(decimal amount)
    {
        var a = Account.Open("user-1", "X", "RUB", Now);
        if (amount > 0) a.Credit(Money.Of(amount, "RUB"));
        return a;
    }

    [Fact]
    public void Transfer_produces_two_balanced_entries()
    {
        var from = Funded(100m);
        var to = Funded(0m);

        var t = Transaction.Transfer("k1", from, to, Money.Of(30m, "RUB"), Now);

        Assert.Equal(TransactionStatus.Completed, t.Status);
        Assert.Equal(70m, from.Balance);
        Assert.Equal(30m, to.Balance);
        Assert.Equal(2, t.Entries.Count);
        Assert.Equal(0m, t.Entries.Sum(e => e.Amount)); // двойная запись: сумма проводок = 0
        Assert.Equal(-30m, t.Entries.Single(e => e.AccountId == from.Id).Amount);
        Assert.Equal(70m, t.Entries.Single(e => e.AccountId == from.Id).BalanceAfter);
        Assert.Equal(30m, t.Entries.Single(e => e.AccountId == to.Id).BalanceAfter);
    }

    [Fact]
    public void Insufficient_funds_yields_rejected_transaction_without_entries_or_balance_change()
    {
        var from = Funded(10m);
        var to = Funded(0m);

        var t = Transaction.Transfer("k1", from, to, Money.Of(11m, "RUB"), Now);

        Assert.Equal(TransactionStatus.Rejected, t.Status);
        Assert.Equal("funds.insufficient", t.RejectionCode);
        Assert.Empty(t.Entries);
        Assert.Equal(10m, from.Balance);
        Assert.Equal(0m, to.Balance);
    }

    [Fact]
    public void Frozen_recipient_does_not_debit_sender()
    {
        var from = Funded(100m);
        var to = Funded(0m);
        to.Freeze();

        var t = Transaction.Transfer("k1", from, to, Money.Of(5m, "RUB"), Now);

        Assert.Equal(TransactionStatus.Rejected, t.Status);
        Assert.Equal("account.inactive", t.RejectionCode);
        Assert.Equal(100m, from.Balance); // проверки идут до мутаций
    }

    [Fact]
    public void Transfer_to_same_account_is_a_hard_error()
    {
        var a = Funded(100m);
        Assert.Throws<DomainException>(() => Transaction.Transfer("k1", a, a, Money.Of(1m, "RUB"), Now));
    }

    [Fact]
    public void Deposit_and_withdrawal_have_single_signed_entry()
    {
        var a = Funded(0m);
        var d = Transaction.Deposit("d", a, Money.Of(50m, "RUB"), Now);
        var w = Transaction.Withdraw("w", a, Money.Of(20m, "RUB"), Now);

        Assert.Equal(50m, d.Entries.Single().Amount);
        Assert.Equal(-20m, w.Entries.Single().Amount);
        Assert.Equal(30m, a.Balance);
        Assert.Equal(30m, w.Entries.Single().BalanceAfter);
    }
}
