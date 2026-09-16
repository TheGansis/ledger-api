namespace Ledger.Application.Events;

/// <summary>
/// Контракт события для внешних потребителей. Версионируется именем маршрута
/// (transaction.completed / transaction.rejected) и полем Version — чтобы потребители
/// могли пережить изменение схемы.
/// </summary>
public sealed record TransactionRecordedEvent(
    int Version,
    Guid TransactionId,
    string Type,
    string Status,
    decimal Amount,
    string Currency,
    Guid? FromAccountId,
    Guid? ToAccountId,
    string? RejectionCode,
    DateTimeOffset OccurredAt)
{
    public const string CompletedRoute = "transaction.completed";
    public const string RejectedRoute = "transaction.rejected";
    public string Route => Status == "Completed" ? CompletedRoute : RejectedRoute;
}
