using System.Text;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledger.Infrastructure.Outbox;

/// <summary>
/// Публикатор outbox → RabbitMQ. Гарантия — at-least-once: если упали между publish и
/// отметкой ProcessedAt, сообщение уйдёт повторно, поэтому потребители обязаны быть идемпотентными
/// (дедупликация по MessageId). Порядок сохраняется в пределах одного публикатора (ORDER BY id).
/// FOR UPDATE SKIP LOCKED позволяет запускать несколько экземпляров API без двойной публикации.
/// </summary>
public sealed class OutboxPublisher(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> options, ILogger<OutboxPublisher> log) : BackgroundService
{
    private readonly RabbitMqOptions _opt = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EnsureChannelAsync(ct);
                var published = await PublishBatchAsync(ct);
                if (published == 0) await Task.Delay(_opt.PollIntervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // Брокер недоступен — не роняем API: события накапливаются в outbox и уйдут позже.
                log.LogWarning(ex, "Outbox publish failed, retrying in 5s");
                await ResetChannelAsync();
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // SKIP LOCKED: параллельный публикатор возьмёт следующие строки, а не будет ждать наши.
            var batch = await db.Set<OutboxMessage>()
                .FromSqlInterpolated($"SELECT * FROM ledger.outbox_messages WHERE processed_at IS NULL ORDER BY id LIMIT {_opt.BatchSize} FOR UPDATE SKIP LOCKED")
                .ToListAsync(ct);
            if (batch.Count == 0) return 0;

            foreach (var msg in batch)
            {
                try
                {
                    var props = new BasicProperties
                    {
                        MessageId = msg.Id.ToString(),          // ключ дедупликации для потребителей
                        ContentType = "application/json",
                        DeliveryMode = DeliveryModes.Persistent, // переживает рестарт брокера
                        Timestamp = new AmqpTimestamp(msg.OccurredAt.ToUnixTimeSeconds()),
                        Type = msg.Route,
                    };
                    await _channel!.BasicPublishAsync(_opt.Exchange, msg.Route, mandatory: false, props,
                        Encoding.UTF8.GetBytes(msg.Payload), ct);
                    msg.ProcessedAt = DateTimeOffset.UtcNow;
                    msg.LastError = null;
                }
                catch (Exception ex)
                {
                    msg.Attempts++;
                    msg.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                    log.LogError(ex, "Failed to publish outbox message {Id} (attempt {Attempts})", msg.Id, msg.Attempts);
                    throw;
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            log.LogDebug("Published {Count} outbox messages", batch.Count);
            return batch.Count;
        });
    }

    private async Task EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return;
        await ResetChannelAsync();

        var factory = new ConnectionFactory
        {
            HostName = _opt.Host, Port = _opt.Port, UserName = _opt.User, Password = _opt.Password,
            ClientProvidedName = "ledger-api-outbox",
        };
        _connection = await factory.CreateConnectionAsync(ct);
        // Publisher confirms: BasicPublishAsync завершится только когда брокер подтвердил приём.
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await _channel.ExchangeDeclareAsync(_opt.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        log.LogInformation("Outbox publisher connected to RabbitMQ {Host}:{Port}, exchange {Exchange}", _opt.Host, _opt.Port, _opt.Exchange);
    }

    private async Task ResetChannelAsync()
    {
        try { if (_channel is not null) await _channel.DisposeAsync(); } catch { /* ignore */ }
        try { if (_connection is not null) await _connection.DisposeAsync(); } catch { /* ignore */ }
        _channel = null; _connection = null;
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        await ResetChannelAsync();
    }
}
