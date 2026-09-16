using System.Text.Json;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Caching;

/// <summary>
/// Cache-aside поверх IDistributedCache (Redis). TTL короткий — кэш лишь снимает нагрузку
/// с БД на горячих чтениях; источник правды — PostgreSQL. Любая ошибка Redis логируется и
/// проглатывается: недоступный кэш не должен ронять чтение счёта.
/// </summary>
public sealed class RedisAccountCache(IDistributedCache cache, ILogger<RedisAccountCache> log) : IAccountCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Key(Guid id) => $"account:{id}"; // InstanceName "ledger:" добавляется автоматически

    public async Task<AccountDto?> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var bytes = await cache.GetAsync(Key(id), ct);
            return bytes is null ? null : JsonSerializer.Deserialize<AccountDto>(bytes, Json);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Account cache read failed for {AccountId}; falling back to database", id);
            return null;
        }
    }

    public async Task SetAsync(AccountDto account, CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(Key(account.Id), JsonSerializer.SerializeToUtf8Bytes(account, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl }, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Account cache write failed for {AccountId}", account.Id);
        }
    }

    public async Task InvalidateAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            try { await cache.RemoveAsync(Key(id), ct); }
            catch (Exception ex) { log.LogWarning(ex, "Account cache invalidation failed for {AccountId}", id); }
        }
    }
}
