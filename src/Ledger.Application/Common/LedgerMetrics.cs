using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ledger.Application.Common;

/// <summary>
/// Бизнес-метрики и трассировка через стандартные System.Diagnostics — без зависимости от
/// OpenTelemetry в Application. Экспортёр (Prometheus/OTLP) подключается в Api.
/// </summary>
public static class LedgerMetrics
{
    public const string MeterName = "Ledger";
    public static readonly ActivitySource ActivitySource = new(MeterName);
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Transactions = Meter.CreateCounter<long>(
        "ledger.transactions", unit: "{transaction}", description: "Финансовые операции по типу и статусу");
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "ledger.operation.duration", unit: "ms", description: "Длительность денежной операции от запроса до коммита");
    private static readonly Counter<long> IdempotentReplays = Meter.CreateCounter<long>(
        "ledger.idempotency.replays", unit: "{request}", description: "Повторы запросов по Idempotency-Key");
    private static readonly Counter<long> IdempotencyConflicts = Meter.CreateCounter<long>(
        "ledger.idempotency.conflicts", unit: "{request}", description: "Ключ повторён с другим телом (409)");

    public static void TransactionRecorded(string type, string status, string? rejectionCode) =>
        Transactions.Add(1, new("type", type), new("status", status), new("reason", rejectionCode ?? "none"));

    public static void OperationCompleted(string type, double elapsedMs, bool replayed) =>
        OperationDuration.Record(elapsedMs, new("type", type), new("replayed", replayed));

    public static void Replayed() => IdempotentReplays.Add(1);
    public static void Conflict() => IdempotencyConflicts.Add(1);

    // Outbox: значения обновляет публикатор, gauge читает их при скрейпе.
    private static long _outboxPending;
    private static double _outboxLagSeconds;
    public static void OutboxObserved(long pending, double lagSeconds) { _outboxPending = pending; _outboxLagSeconds = lagSeconds; }
    static LedgerMetrics()
    {
        Meter.CreateObservableGauge("ledger.outbox.pending", () => _outboxPending, "{message}", "Неопубликованные сообщения outbox");
        Meter.CreateObservableGauge("ledger.outbox.lag", () => _outboxLagSeconds, "s", "Возраст самого старого неопубликованного сообщения");
    }
}
