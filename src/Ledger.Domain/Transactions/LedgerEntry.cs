namespace Ledger.Domain.Transactions;

/// <summary>
/// Проводка — неизменяемая запись об изменении баланса одного счёта.
/// Перевод порождает две проводки (дебет одного счёта и кредит другого) — двойная запись:
/// сумма проводок по любой завершённой транзакции внутри системы равна нулю.
/// </summary>
public sealed class LedgerEntry
{
    private LedgerEntry() { }

    internal LedgerEntry(Guid transactionId, Guid accountId, decimal amount, decimal balanceAfter, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        TransactionId = transactionId;
        AccountId = accountId;
        Amount = amount;
        BalanceAfter = balanceAfter;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }

    /// <summary>Знаковая сумма: &lt;0 — списание, &gt;0 — зачисление.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Баланс счёта после проводки — для выписки и сверки.</summary>
    public decimal BalanceAfter { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Монотонный номер для курсорной пагинации (bigint identity в БД).</summary>
    public long Sequence { get; private set; }
}
