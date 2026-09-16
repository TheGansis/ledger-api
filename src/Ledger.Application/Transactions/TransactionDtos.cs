using Ledger.Domain.Transactions;

namespace Ledger.Application.Transactions;

public sealed record MoneyOperationRequest(decimal Amount);
public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount);

public sealed record TransactionDto(
    Guid Id, string Type, string Status, decimal Amount, string Currency,
    Guid? FromAccountId, Guid? ToAccountId, string? RejectionCode, string? RejectionReason, DateTimeOffset CreatedAt)
{
    public static TransactionDto From(Transaction t) => new(
        t.Id, t.Type.ToString(), t.Status.ToString(), t.Amount, t.Currency,
        t.FromAccountId, t.ToAccountId, t.RejectionCode, t.RejectionReason, t.CreatedAt);
}

/// <summary>Результат идемпотентной операции: создана сейчас или возвращена уже существующая.</summary>
public sealed record TransactionResult(TransactionDto Transaction, bool Replayed);
