using System.Text;
using System.Text.Json;
using Ledger.Application.Events;
using Ledger.Infrastructure.Outbox;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ledger.Api.Tests;

[Collection(ApiCollection.Name)]
public class OutboxTests(LedgerApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClientFor("user-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // payload — jsonb: обычный LIKE к нему неприменим, ищем по JSON-полю оператором ->>.
    private static IQueryable<OutboxMessage> ByTransaction(LedgerDbContext db, Guid transactionId) =>
        db.Set<OutboxMessage>().FromSqlInterpolated(
            $"SELECT * FROM ledger.outbox_messages WHERE payload->>'transactionId' = {transactionId.ToString()}").AsNoTracking();

    [Fact]
    public async Task Transfer_writes_outbox_message_in_the_same_transaction()
    {
        var a = await _http.OpenAccountAsync("A");
        var b = await _http.OpenAccountAsync("B");
        await _http.DepositAsync(a.Id, 100m, ApiClientExtensions.Key());
        var t = await (await _http.TransferAsync(a.Id, b.Id, 25m, ApiClientExtensions.Key())).AsTransactionAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var msg = await ByTransaction(db, t.Id).SingleAsync();

        Assert.Equal(TransactionRecordedEvent.CompletedRoute, msg.Route);
        var evt = JsonSerializer.Deserialize<TransactionRecordedEvent>(msg.Payload, Json)!;
        Assert.Equal(t.Id, evt.TransactionId);
        Assert.Equal(25m, evt.Amount);
        Assert.Equal(a.Id, evt.FromAccountId);
        Assert.Equal(b.Id, evt.ToAccountId);
    }

    [Fact]
    public async Task Rejected_transfer_is_published_on_rejected_route()
    {
        var a = await _http.OpenAccountAsync("A");
        var b = await _http.OpenAccountAsync("B");
        var t = await (await _http.TransferAsync(a.Id, b.Id, 1m, ApiClientExtensions.Key())).AsTransactionAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var msg = await ByTransaction(db, t.Id).SingleAsync();

        Assert.Equal("Rejected", t.Status);
        Assert.Equal(TransactionRecordedEvent.RejectedRoute, msg.Route);
        Assert.Contains("funds.insufficient", msg.Payload);
    }

    [Fact]
    public async Task Replayed_request_does_not_produce_second_event()
    {
        var a = await _http.OpenAccountAsync("A");
        var key = ApiClientExtensions.Key();
        var t = await (await _http.DepositAsync(a.Id, 5m, key)).AsTransactionAsync();
        await _http.DepositAsync(a.Id, 5m, key);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        Assert.Equal(1, await ByTransaction(db, t.Id).CountAsync());
    }

    [Fact]
    public async Task Event_is_delivered_to_rabbitmq_with_message_id_and_persistent_flag()
    {
        // Подписываемся на временную очередь ДО операции, чтобы не пропустить публикацию.
        var cf = new ConnectionFactory { HostName = "localhost" };
        await using var conn = await cf.CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();
        await ch.ExchangeDeclareAsync("ledger.events", ExchangeType.Topic, durable: true, autoDelete: false);
        var q = (await ch.QueueDeclareAsync()).QueueName; // exclusive, auto-delete
        await ch.QueueBindAsync(q, "ledger.events", "transaction.completed");

        var received = new TaskCompletionSource<BasicDeliverEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = await _http.OpenAccountAsync("A");
        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += (_, ea) =>
        {
            if (Encoding.UTF8.GetString(ea.Body.Span).Contains(a.Id.ToString())) received.TrySetResult(ea);
            return Task.CompletedTask;
        };
        await ch.BasicConsumeAsync(q, autoAck: true, consumer);

        var t = await (await _http.DepositAsync(a.Id, 42m, ApiClientExtensions.Key())).AsTransactionAsync();

        var ea = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var evt = JsonSerializer.Deserialize<TransactionRecordedEvent>(Encoding.UTF8.GetString(ea.Body.Span), Json)!;
        Assert.Equal(t.Id, evt.TransactionId);
        Assert.Equal(42m, evt.Amount);
        Assert.False(string.IsNullOrEmpty(ea.BasicProperties.MessageId));
        Assert.Equal(DeliveryModes.Persistent, ea.BasicProperties.DeliveryMode);
        Assert.Equal("application/json", ea.BasicProperties.ContentType);

        // После публикации сообщение помечено обработанным
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var msg = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == long.Parse(ea.BasicProperties.MessageId!));
        Assert.NotNull(msg.ProcessedAt);
    }
}
