using FluentValidation;
using Ledger.Application.Abstractions;
using Ledger.Application.Common;
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
public sealed class TransactionService(IAccountRepository accounts, ITransactionRepository transactions, IUnitOfWork uow, IClock clock)
{
    public Task<TransactionResult> DepositAsync(string key, Guid accountId, MoneyOperationRequest req, CancellationToken ct) =>
        RunAsync(key, [accountId], Fingerprint("deposit", accountId, req.Amount), locked =>
        {
            var to = Require(locked, accountId);
            return Transaction.Deposit(key, to, Money.Of(req.Amount, to.Currency), clock.UtcNow);
        }, ct);

    public Task<TransactionResult> WithdrawAsync(string key, Guid accountId, MoneyOperationRequest req, CancellationToken ct) =>
        RunAsync(key, [accountId], Fingerprint("withdraw", accountId, req.Amount), locked =>
        {
            var from = Require(locked, accountId);
            return Transaction.Withdraw(key, from, Money.Of(req.Amount, from.Currency), clock.UtcNow);
        }, ct);

    public Task<TransactionResult> TransferAsync(string key, TransferRequest req, CancellationToken ct) =>
        RunAsync(key, [req.FromAccountId, req.ToAccountId], Fingerprint("transfer", req.FromAccountId, req.ToAccountId, req.Amount), locked =>
        {
            var from = Require(locked, req.FromAccountId);
            var to = Require(locked, req.ToAccountId);
            return Transaction.Transfer(key, from, to, Money.Of(req.Amount, from.Currency), clock.UtcNow);
        }, ct);

    public async Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await transactions.FindAsync(id, ct);
        return t is null ? null : TransactionDto.From(t);
    }

    private async Task<TransactionResult> RunAsync(string key, Guid[] accountIds, string fingerprint,
        Func<IReadOnlyDictionary<Guid, Account>, Transaction> operation, CancellationToken ct)
    {
        // Быстрый путь: повтор уже завершённого запроса не должен брать блокировки.
        var existing = await transactions.FindByIdempotencyKeyAsync(key, ct);
        if (existing is not null) return Replay(existing, fingerprint);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var locked = await accounts.GetForUpdateAsync(accountIds, token);

            // Повторная проверка под блокировкой: гонка двух одинаковых запросов.
            var raced = await transactions.FindByIdempotencyKeyAsync(key, token);
            if (raced is not null) return Replay(raced, fingerprint);

            var transaction = operation(locked);
            transaction.SetFingerprint(fingerprint);
            transactions.Add(transaction);
            await uow.SaveChangesAsync(token);
            return new TransactionResult(TransactionDto.From(transaction), Replayed: false);
        }, ct);
    }

    private static TransactionResult Replay(Transaction existing, string fingerprint)
    {
        if (existing.RequestFingerprint != fingerprint)
            throw new IdempotencyConflictException(existing.IdempotencyKey);
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
