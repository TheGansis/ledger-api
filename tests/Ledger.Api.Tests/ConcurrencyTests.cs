using System.Net;
using Ledger.Application.Transactions;

namespace Ledger.Api.Tests;

/// <summary>
/// Главное, ради чего база настоящая: параллельные переводы не должны терять и создавать деньги.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(LedgerApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClientFor("user-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Parallel_transfers_in_both_directions_preserve_total_and_never_deadlock()
    {
        var a = await _http.OpenAccountAsync("A");
        var b = await _http.OpenAccountAsync("B");
        await _http.DepositAsync(a.Id, 1000m, ApiClientExtensions.Key());
        await _http.DepositAsync(b.Id, 1000m, ApiClientExtensions.Key());

        const int n = 100;
        // Чётные — A→B, нечётные — B→A: классический сценарий взаимной блокировки при неупорядоченных lock'ах.
        var tasks = Enumerable.Range(0, n).Select(i => i % 2 == 0
            ? _http.TransferAsync(a.Id, b.Id, 7m, ApiClientExtensions.Key())
            : _http.TransferAsync(b.Id, a.Id, 3m, ApiClientExtensions.Key()));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var results = await Task.WhenAll(responses.Select(r => r.AsTransactionAsync()));
        Assert.All(results, t => Assert.Equal("Completed", t.Status));

        var fa = await _http.GetAccountAsync(a.Id);
        var fb = await _http.GetAccountAsync(b.Id);
        Assert.Equal(2000m, fa.Balance + fb.Balance);            // деньги не появились и не исчезли
        Assert.Equal(1000m - 50 * 7m + 50 * 3m, fa.Balance);       // 800
        Assert.Equal(1000m + 50 * 7m - 50 * 3m, fb.Balance);       // 1200
    }

    [Fact]
    public async Task Parallel_withdrawals_cannot_overdraw()
    {
        var acc = await _http.OpenAccountAsync();
        await _http.DepositAsync(acc.Id, 100m, ApiClientExtensions.Key());

        // 30 попыток снять по 10 при балансе 100: ровно 10 должны пройти, 20 — отклониться.
        var responses = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => _http.WithdrawAsync(acc.Id, 10m, ApiClientExtensions.Key())));
        var results = await Task.WhenAll(responses.Select(r => r.AsTransactionAsync()));

        Assert.Equal(10, results.Count(t => t.Status == "Completed"));
        Assert.Equal(20, results.Count(t => t.RejectionCode == "funds.insufficient"));
        Assert.Equal(0m, (await _http.GetAccountAsync(acc.Id)).Balance);
    }

    [Fact]
    public async Task Same_idempotency_key_sent_concurrently_creates_exactly_one_transaction()
    {
        var acc = await _http.OpenAccountAsync();
        var key = ApiClientExtensions.Key();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => _http.DepositAsync(acc.Id, 5m, key)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        var results = await Task.WhenAll(responses.Select(r => r.AsTransactionAsync()));
        Assert.Single(results.Select(t => t.Id).Distinct());
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5m, (await _http.GetAccountAsync(acc.Id)).Balance);
    }
}
