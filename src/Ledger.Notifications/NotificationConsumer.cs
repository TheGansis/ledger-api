using System.Text;
using System.Text.Json;
using Ledger.Application.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace Ledger.Notifications;

/// <summary>
/// Потребитель событий транзакций. Доставка из outbox — at-least-once, поэтому консьюмер
/// идемпотентен: MessageId запоминается в Redis (SET NX с TTL), повтор пропускается.
/// Здесь «уведомление» — запись в лог; в реальной системе — push/email/SMS-шлюз.
/// </summary>
public sealed class NotificationConsumer(IConfiguration config, IConnectionMultiplexer redis, ILogger<NotificationConsumer> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DedupeTtl = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var host = config["RabbitMq:Host"] ?? "localhost";
        var exchange = config["RabbitMq:Exchange"] ?? "ledger.events";
        var queue = config["RabbitMq:Queue"] ?? "ledger.notifications";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = host, Port = config.GetValue("RabbitMq:Port", 5672),
                    UserName = config["RabbitMq:User"] ?? "guest", Password = config["RabbitMq:Password"] ?? "guest",
                    ClientProvidedName = "ledger-notifications",
                };
                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

                await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueBindAsync(queue, exchange, "transaction.*", cancellationToken: ct);
                // Prefetch: не забирать всю очередь в память одного экземпляра — по 20 сообщений на воркер.
                await channel.BasicQosAsync(0, 20, false, ct);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    try
                    {
                        await HandleAsync(ea, ct);
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                    }
                    catch (Exception ex)
                    {
                        // Повторная доставка после сбоя; при систематической ошибке — DLQ (см. РАЗБОР.md).
                        log.LogError(ex, "Failed to handle message {MessageId}; requeue", ea.BasicProperties.MessageId);
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
                    }
                };
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer, ct);
                log.LogInformation("Consuming {Queue} bound to {Exchange}/transaction.*", queue, exchange);

                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogWarning(ex, "RabbitMQ connection lost, reconnecting in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task HandleAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var messageId = ea.BasicProperties.MessageId ?? throw new InvalidOperationException("MessageId is required for deduplication");
        var db = redis.GetDatabase();

        // SET NX: атомарно «пометить как обработанное, если ещё не было». Повтор — пропускаем.
        if (!await db.StringSetAsync($"ledger:notifications:seen:{messageId}", "1", DedupeTtl, When.NotExists))
        {
            log.LogInformation("Duplicate message {MessageId} skipped", messageId);
            return;
        }

        var evt = JsonSerializer.Deserialize<TransactionRecordedEvent>(Encoding.UTF8.GetString(ea.Body.Span), Json)
                  ?? throw new InvalidOperationException("Empty payload");

        switch (ea.RoutingKey)
        {
            case TransactionRecordedEvent.CompletedRoute:
                if (evt.FromAccountId is { } from)
                    log.LogInformation("→ owner of {Account}: списание {Amount} {Currency} ({Type}, tx {Tx})", from, evt.Amount, evt.Currency, evt.Type, evt.TransactionId);
                if (evt.ToAccountId is { } to)
                    log.LogInformation("→ owner of {Account}: зачисление {Amount} {Currency} ({Type}, tx {Tx})", to, evt.Amount, evt.Currency, evt.Type, evt.TransactionId);
                break;
            case TransactionRecordedEvent.RejectedRoute:
                log.LogInformation("→ owner of {Account}: операция {Type} на {Amount} {Currency} отклонена: {Code}",
                    evt.FromAccountId ?? evt.ToAccountId, evt.Type, evt.Amount, evt.Currency, evt.RejectionCode);
                break;
            default:
                log.LogWarning("Unknown route {Route}", ea.RoutingKey);
                break;
        }
    }
}
