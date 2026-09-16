using System.Net;
using System.Net.Http.Json;
using Ledger.Application.Transactions;

namespace Ledger.Api.Tests;

[Collection(ApiCollection.Name)]
public class TransfersTests(LedgerApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClientFor("user-" + Guid.NewGuid().ToString("N"));

    private async Task<(Guid from, Guid to)> TwoAccountsAsync(decimal funded)
    {
        var a = await _http.OpenAccountAsync("A");
        var b = await _http.OpenAccountAsync("B");
        if (funded > 0) await _http.DepositAsync(a.Id, funded, ApiClientExtensions.Key());
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task Transfer_moves_money_and_is_readable_by_id()
    {
        var (from, to) = await TwoAccountsAsync(100m);

        var resp = await _http.TransferAsync(from, to, 40m, ApiClientExtensions.Key());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var t = await resp.AsTransactionAsync();
        Assert.Equal("Completed", t.Status);
        Assert.Equal(60m, (await _http.GetAccountAsync(from)).Balance);
        Assert.Equal(40m, (await _http.GetAccountAsync(to)).Balance);

        var byId = await _http.GetFromJsonAsync<TransactionDto>($"/api/transactions/{t.Id}");
        Assert.Equal(t.Id, byId!.Id);
    }

    [Fact]
    public async Task Same_key_replays_same_transaction_with_200_and_no_double_charge()
    {
        var (from, to) = await TwoAccountsAsync(100m);
        var key = ApiClientExtensions.Key();

        var first = await _http.TransferAsync(from, to, 10m, key);
        var second = await _http.TransferAsync(from, to, 10m, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await first.AsTransactionAsync()).Id, (await second.AsTransactionAsync()).Id);
        Assert.Equal(90m, (await _http.GetAccountAsync(from)).Balance);
    }

    [Fact]
    public async Task Same_key_with_different_payload_returns_409()
    {
        var (from, to) = await TwoAccountsAsync(100m);
        var key = ApiClientExtensions.Key();
        await _http.TransferAsync(from, to, 10m, key);

        var resp = await _http.TransferAsync(from, to, 11m, key);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains("idempotency.conflict", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rejected_transfer_replays_as_rejected()
    {
        var (from, to) = await TwoAccountsAsync(5m);
        var key = ApiClientExtensions.Key();

        var first = await (await _http.TransferAsync(from, to, 50m, key)).AsTransactionAsync();
        await _http.DepositAsync(from, 100m, ApiClientExtensions.Key()); // теперь денег хватило бы
        var replay = await (await _http.TransferAsync(from, to, 50m, key)).AsTransactionAsync();

        Assert.Equal("Rejected", first.Status);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal("Rejected", replay.Status); // повтор не выполняет операцию заново
        Assert.Equal(105m, (await _http.GetAccountAsync(from)).Balance);
    }

    [Fact]
    public async Task Transfer_between_different_currencies_is_rejected()
    {
        var rub = await _http.OpenAccountAsync("R", "RUB");
        var usd = await _http.OpenAccountAsync("U", "USD");
        await _http.DepositAsync(rub.Id, 100m, ApiClientExtensions.Key());

        var t = await (await _http.TransferAsync(rub.Id, usd.Id, 1m, ApiClientExtensions.Key())).AsTransactionAsync();

        Assert.Equal("Rejected", t.Status);
        Assert.Equal("currency.mismatch", t.RejectionCode);
    }

    [Fact]
    public async Task Transfer_to_unknown_account_returns_404()
    {
        var (from, _) = await TwoAccountsAsync(100m);
        var resp = await _http.TransferAsync(from, Guid.NewGuid(), 1m, ApiClientExtensions.Key());
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Transfer_to_self_returns_400()
    {
        var (from, _) = await TwoAccountsAsync(100m);
        var resp = await _http.TransferAsync(from, from, 1m, ApiClientExtensions.Key());
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
