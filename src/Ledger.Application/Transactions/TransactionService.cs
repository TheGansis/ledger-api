using System.Diagnostics;
using FluentValidation;
using Ledger.Application.Abstractions;
using Ledger.Application.Common;
using Ledger.Application.Events;
using Ledger.Domain.Accounts;
using Ledger.Domain.Common;
using Ledger.Domain.Transactions;

namespace Ledger.Application.Transactions;

public sealed class MoneyOperationValidator : AbstractValidator<MoneyOperationRequest>
{
    public MoneyOperationValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Scale, ignoreTrailingZeros: true);
    }
}

public sealed class TransferValidator : AbstractValidator<TransferRequest>
{
    public TransferValidator()
    {
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.ToAccountId).NotEmpty().NotEqual(x => x.FromAccountId).WithMessage("Cannot transfer to the same account.");
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(19, Money.Scale, ignoreTrailingZeros: true);
    }
}

/// <summary>
/// Все денежные операции проходят через один шаблон:
/// 1) вне транзакции ищем ключ идемпотентности — дешёвый быстрый путь для повторов;
/// 2) в транзакции блокируем счета (FOR UPDATE, порядок по id), повторно проверяем ключ
///    (два одновременных запроса с одним ключом: второй увидит первый после его коммита
///    либо упадёт на unique-индексе — оба случая обработаны);
/// 3) применяем доменную операцию, сохраняем транзакцию + проводки + балансы атомарно.
/// </summary>
public sealed class TransactionService(IAccountRepository accounts, ITransactionRepository transactions, IUnitOfWork uow, IClock clock, IOutbox outbox, IAccountCache cache, ICurrentUser user)
{
    public Task<TransactionResult> DepositAsync(string key, Guid accountId, MoneyOperationRequest req, CancellationToken ct) =>
        RunAsync(key, [accountId], Fingerprint("deposit", accountId, req.Amount), locked =>
        {
            var to = Require(locked, accountId);
            EnsureOwner(to); // пополнить можно только свой счёт (или админ — любой)
            return Transaction.Deposit(key, to, Money.Of(req.Amount, to.Currency), clock.UtcNow);
        }, ct);

    public Task<TransactionResult> WithdrawAsync(string key, Guid accountId, MoneyOperationRequest req, CancellationToken ct) =>
        RunAsync(key, [accountId], Fingerprint("withdraw", accountId, req.Amount), locked =>
        {
            var from = Require(locked, accountId);
            EnsureOwner(from);
            return Transaction.Withdraw(key, from, Money.Of(req.Amount, from.Currency), clock.UtcNow);
        }, ct);

    public Task<TransactionResult> TransferAsync(string key, TransferRequest req, CancellationToken ct) =>
        RunAsync(key, [req.FromAccountId, req.ToAccountId], Fingerprint("transfer", req.FromAccountId, req.ToAccountId, req.Amount), locked =>
        {
            var from = Require(locked, req.FromAccountId);
            var to = Require(locked, req.ToAccountId);
            EnsureOwner(from); // получатель может быть чужим — это и есть перевод
            return Transaction.Transfer(key, from, to, Money.Of(req.Amount, from.Currency), clock.UtcNow);
        }, ct);

    public async Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await transactions.FindAsync(id, ct);
        if (t is null) return null;
        if (!user.IsAdmin)
        {
            // Участник транзакции — владелец любого из её счетов.
            var ids = new[] { t.FromAccountId, t.ToAccountId }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            var involved = false;
            foreach (var id2 in ids)
                if (await accounts.FindAsync(id2, ct) is { } a && a.IsOwnedBy(user.Subject)) { involved = true; break; }
            if (!involved) throw new ForbiddenException("Transaction belongs to other accounts.");
        }
        return TransactionDto.From(t);
    }

    private async Task<TransactionResult> RunAsync(string key, Guid[] accountIds, string fingerprint,
        Func<IReadOnlyDictionary<Guid, Account>, Transaction> operation, CancellationToken ct)
    {
        var opType = fingerprint[..fingerprint.IndexOf('|')];
        using var activity = LedgerMetrics.ActivitySource.StartActivity($"ledger.{opType}");
        activity?.SetTag("ledger.idempotency_key", key);
        var sw = Stopwatch.StartNew();

        // Быстрый путь: повтор уже завершённого запроса не должен брать блокировки.
        var existing = await transactions.FindByIdempotencyKeyAsync(key, ct);
        if (existing is not null)
        {
            var replay = Replay(existing, fingerprint);
            LedgerMetrics.OperationCompleted(opType, sw.Elapsed.TotalMilliseconds, replayed: true);
            return replay;
        }

        var result = await uow.ExecuteInTransactionAsync(async token =>
        {
            var locked = await accounts.GetForUpdateAsync(accountIds, token);

            // Повторная проверка под блокировкой: гонка двух одинаковых запросов.
            var raced = await transactions.FindByIdempotencyKeyAsync(key, token);
            if (raced is not null) return Replay(raced, fingerprint);

            var transaction = operation(locked);
            transaction.SetFingerprint(fingerprint);
            transactions.Add(transaction);

            // Событие — в той же транзакции, что и проводки (outbox).
            var evt = new TransactionRecordedEvent(1, transaction.Id, transaction.Type.ToString(), transaction.Status.ToString(),
                transaction.Amount, transaction.Currency, transaction.FromAccountId, transaction.ToAccountId,
                transaction.RejectionCode, transaction.CreatedAt);
            outbox.Enqueue(evt.Route, evt);

            await uow.SaveChangesAsync(token);
            return new TransactionResult(TransactionDto.From(transaction), Replayed: false);
        }, ct);

        // Инвалидация после коммита: если сделать до — параллельный читатель успеет положить в кэш старый баланс.
        if (!result.Replayed)
        {
            await cache.InvalidateAsync(accountIds, ct);
            LedgerMetrics.TransactionRecorded(result.Transaction.Type, result.Transaction.Status, result.Transaction.RejectionCode);
        }
        activity?.SetTag("ledger.transaction_id", result.Transaction.Id);
        activity?.SetTag("ledger.status", result.Transaction.Status);
        LedgerMetrics.OperationCompleted(opType, sw.Elapsed.TotalMilliseconds, result.Replayed);
        return result;
    }

    private void EnsureOwner(Account account)
    {
        if (!user.IsAdmin && !account.IsOwnedBy(user.Subject))
            throw new ForbiddenException($"Account {account.Id} belongs to another owner.");
    }

    private static TransactionResult Replay(Transaction existing, string fingerprint)
    {
        if (existing.RequestFingerprint != fingerprint)
        {
            LedgerMetrics.Conflict();
            throw new IdempotencyConflictException(existing.IdempotencyKey);
        }
        LedgerMetrics.Replayed();
        return new TransactionResult(TransactionDto.From(existing), Replayed: true);
    }

    private static Account Require(IReadOnlyDictionary<Guid, Account> locked, Guid id) =>
        locked.TryGetValue(id, out var a) ? a : throw new AccountNotFoundException(id);

    private static string Fingerprint(params object[] parts) => string.Join("|", parts.Select(p => p switch
    {
        decimal d => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        _ => p.ToString()
    }));
}

public sealed class AccountNotFoundException(Guid id) : Exception($"Account {id} not found.")
{
    public Guid AccountId { get; } = id;
}
