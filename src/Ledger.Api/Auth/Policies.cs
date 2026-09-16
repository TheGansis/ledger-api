using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ledger.Api.Auth;

public static class Policies
{
    public const string Admin = "Admin";
}

public static class RateLimitPolicies
{
    public const string MoneyOps = "money-ops";

    /// <summary>
    /// Лимит денежных операций на пользователя (по subject из JWT), фиксированное окно в минуту.
    /// Защита от залипшего клиента и перебора; глобальные лимиты — на API-gateway.
    /// </summary>
    public static IServiceCollection AddLedgerRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = config.GetValue("RateLimiting:MoneyOpsPerMinute", 300);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(MoneyOps, ctx => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new { status = 429, title = "Too many money operations", code = "rate_limited" }, ct);
            };
        });
        return services;
    }
}
