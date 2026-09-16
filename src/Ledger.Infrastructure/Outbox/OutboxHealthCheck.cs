using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ledger.Infrastructure.Outbox;

/// <summary>
/// Здоровье outbox = отставание публикации. Если самое старое неотправленное сообщение
/// старше порога — брокер недоступен или публикатор встал; это важнее, чем «RabbitMQ отвечает на ping».
/// </summary>
public sealed class OutboxHealthCheck(LedgerDbContext db) : IHealthCheck
{
    public static readonly TimeSpan DegradedAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan UnhealthyAfter = TimeSpan.FromSeconds(60);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var pending = db.Set<OutboxMessage>().Where(m => m.ProcessedAt == null);
        var count = await pending.CountAsync(ct);
        var oldest = await pending.MinAsync(m => (DateTimeOffset?)m.OccurredAt, ct);
        var lag = oldest is null ? TimeSpan.Zero : DateTimeOffset.UtcNow - oldest.Value;

        var data = new Dictionary<string, object> { ["pending"] = count, ["lagSeconds"] = Math.Round(lag.TotalSeconds, 1) };
        if (lag > UnhealthyAfter) return HealthCheckResult.Unhealthy($"outbox lag {lag.TotalSeconds:0}s, {count} pending", data: data);
        if (lag > DegradedAfter) return HealthCheckResult.Degraded($"outbox lag {lag.TotalSeconds:0}s, {count} pending", data: data);
        return HealthCheckResult.Healthy($"{count} pending", data);
    }
}
