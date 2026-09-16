using Ledger.Domain.Accounts;
using Ledger.Domain.Common;

namespace Ledger.Domain.Transactions;

/// <summary>
/// Финансовая операция. Создаётся один раз на ключ идемпотентности и никогда не меняется:
/// повторный запрос с тем же ключом возвращает ту же транзакцию (в т.ч. отклонённую).
/// </summary>
public sealed class Transaction
{
    private readonly List<LedgerEntry> _entries = [];

    private Transaction() { }

    private Transaction(Guid id, string idempotencyKey, TransactionType type, Money amount,
        Guid? fromAccountId, Guid? toAccountId, DateTimeOffset createdAt)
    {
        Id = id;
        IdempotencyKey = idempotencyKey;
        Type = type;
        Amount = amount.Amount;
        Currency = amount.Currency;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        CreatedAt = createdAt;
        Status = TransactionStatus.Completed;
    }

    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public Guid? FromAccountId { get; private set; }
    public Guid? ToAccountId { get; private set; }
    public TransactionStatus Status { get; private set; }
    public string? RejectionCode { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Отпечаток исходного запроса (тип + счета + сумма). Повтор с тем же ключом, но другим
    /// телом — конфликт, а не replay. Хранится вместе с транзакцией, чтобы проверка была атомарной.
    /// </summary>
    public string RequestFingerprint { get; private set; } = "";

    public void SetFingerprint(string fingerprint) => RequestFingerprint = fingerprint;

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public static Transaction Deposit(string idempotencyKey, Account to, Money amount, DateTimeOffset now) =>
        Execute(new Transaction(Guid.NewGuid(), idempotencyKey, TransactionType.Deposit, amount, null, to.Id, now),
            t => { to.Credit(amount); t.Post(to, +amount.Amount, now); });

    public static Transaction Withdraw(string idempotencyKey, Account from, Money amount, DateTimeOffset now) =>
        Execute(new Transaction(Guid.NewGuid(), idempotencyKey, TransactionType.Withdrawal, amount, from.Id, null, now),
            t => { from.Debit(amount); t.Post(from, -amount.Amount, now); });

    public static Transaction Transfer(string idempotencyKey, Account from, Account to, Money amount, DateTimeOffset now)
    {
        if (from.Id == to.Id)
            throw new DomainException("transfer.same_account", "Cannot transfer to the same account.");

        return Execute(new Transaction(Guid.NewGuid(), idempotencyKey, TransactionType.Transfer, amount, from.Id, to.Id, now),
            t =>
            {
                // Сначала все проверки, потом все мутации: если получатель заморожен,
                // отправитель не должен остаться со списанным в памяти балансом.
                // Обе проводки попадают в БД в одной транзакции — частичного перевода не бывает.
                from.EnsureCanDebit(amount);
                to.EnsureCanCredit(amount);
                from.Debit(amount);
                to.Credit(amount);
                t.Post(from, -amount.Amount, now);
                t.Post(to, +amount.Amount, now);
            });
    }

    /// <summary>
    /// Бизнес-отказ (нет средств, счёт заморожен) — это не ошибка системы, а результат.
    /// Его фиксируем как Rejected-транзакцию, чтобы ответ на повтор запроса был тем же.
    /// </summary>
    private static Transaction Execute(Transaction t, Action<Transaction> body)
    {
        try
        {
            body(t);
        }
        catch (DomainException ex)
        {
            t.Status = TransactionStatus.Rejected;
            t.RejectionCode = ex.Code;
            t.RejectionReason = ex.Message;
            t._entries.Clear();
        }
        return t;
    }

    private void Post(Account account, decimal signedAmount, DateTimeOffset now) =>
        _entries.Add(new LedgerEntry(Id, account.Id, signedAmount, account.Balance, now));
}
