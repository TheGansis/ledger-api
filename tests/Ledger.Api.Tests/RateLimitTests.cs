using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ledger.Api.Tests;

public class RateLimitTests : IClassFixture<RateLimitTests.LimitedFactory>
{
    /// <summary>Отдельная фабрика с лимитом 3 операции/мин, чтобы не мешать остальным тестам.</summary>
    public sealed class LimitedFactory : LedgerApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimiting:MoneyOpsPerMinute", "3");
        }
    }

    private readonly LimitedFactory _factory;
    public RateLimitTests(LimitedFactory factory) => _factory = factory;

    [Fact]
    public async Task Fourth_money_operation_within_a_minute_is_429_and_other_user_is_unaffected()
    {
        var alice = _factory.CreateClientFor("alice-" + Guid.NewGuid());
        var bob = _factory.CreateClientFor("bob-" + Guid.NewGuid());
        var a = await alice.OpenAccountAsync("Alice");
        var b = await bob.OpenAccountAsync("Bob");

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await alice.DepositAsync(a.Id, 1m, ApiClientExtensions.Key())).StatusCode);

        var limited = await alice.DepositAsync(a.Id, 1m, ApiClientExtensions.Key());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.RetryAfter!.ToString());
        Assert.Contains("rate_limited", await limited.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, (await bob.DepositAsync(b.Id, 1m, ApiClientExtensions.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/accounts/{a.Id}")).StatusCode); // чтение не лимитируется
    }
}
