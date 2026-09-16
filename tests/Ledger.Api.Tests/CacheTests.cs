using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.Tests;

[Collection(ApiCollection.Name)]
public class CacheTests(LedgerApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClientFor("user-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Get_populates_cache_and_money_operation_invalidates_it()
    {
        var a = await _http.OpenAccountAsync("A");
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IAccountCache>();

        Assert.Null(await cache.GetAsync(a.Id, CancellationToken.None));
        await _http.GetAccountAsync(a.Id);
        var cached = await cache.GetAsync(a.Id, CancellationToken.None);
        Assert.NotNull(cached);
        Assert.Equal(0m, cached!.Balance);

        await _http.DepositAsync(a.Id, 10m, ApiClientExtensions.Key());
        Assert.Null(await cache.GetAsync(a.Id, CancellationToken.None)); // инвалидирован после коммита
        Assert.Equal(10m, (await _http.GetAccountAsync(a.Id)).Balance);    // и перечитан из БД
    }

    [Fact]
    public async Task Both_accounts_of_a_transfer_are_invalidated()
    {
        var a = await _http.OpenAccountAsync("A");
        var b = await _http.OpenAccountAsync("B");
        await _http.DepositAsync(a.Id, 100m, ApiClientExtensions.Key());
        await _http.GetAccountAsync(a.Id);
        await _http.GetAccountAsync(b.Id);

        await _http.TransferAsync(a.Id, b.Id, 30m, ApiClientExtensions.Key());

        Assert.Equal(70m, (await _http.GetAccountAsync(a.Id)).Balance);
        Assert.Equal(30m, (await _http.GetAccountAsync(b.Id)).Balance);
    }

    [Fact]
    public async Task Stale_cache_entry_is_not_served_after_status_change()
    {
        var a = await _http.OpenAccountAsync("A");
        await _http.GetAccountAsync(a.Id);
        await factory.CreateClientFor("admin-1", "admin").PostAsync($"/api/accounts/{a.Id}/freeze", null);
        Assert.Equal("Frozen", (await _http.GetAccountAsync(a.Id)).Status);
    }
}
