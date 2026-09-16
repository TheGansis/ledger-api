namespace Ledger.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public long Id { get; set; }                 // identity — порядок публикации = порядок записи
    public string Route { get; set; } = null!;   // routing key в брокере
    public string Payload { get; set; } = null!; // JSON (jsonb в PG)
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
