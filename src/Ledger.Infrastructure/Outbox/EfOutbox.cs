using System.Text.Json;
using Ledger.Application.Abstractions;
using Ledger.Application.Common;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Infrastructure.Outbox;

public sealed class EfOutbox(LedgerDbContext db, IClock clock) : IOutbox
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Enqueue<T>(string route, T payload) where T : notnull =>
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Route = route,
            Payload = JsonSerializer.Serialize(payload, Json),
            OccurredAt = clock.UtcNow,
        });
}
