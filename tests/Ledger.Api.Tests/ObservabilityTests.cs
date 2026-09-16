using System.Net;

namespace Ledger.Api.Tests;

[Collection(ApiCollection.Name)]
public class ObservabilityTests(LedgerApiFactory factory)
{
    [Fact]
    public async Task Metrics_endpoint_exposes_business_counters_after_operations()
    {
        var http = factory.CreateClientFor("metrics-" + Guid.NewGuid());
        var a = await http.OpenAccountAsync("M");
        await http.DepositAsync(a.Id, 1m, ApiClientExtensions.Key());
        var key = ApiClientExtensions.Key();
        await http.DepositAsync(a.Id, 1m, key);
        await http.DepositAsync(a.Id, 1m, key); // replay

        var resp = await factory.CreateClient().GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ledger_transactions", body);
        Assert.Contains("ledger_operation_duration", body);
        Assert.Contains("ledger_idempotency_replays", body);
        Assert.Contains("ledger_outbox_pending", body);
        Assert.Contains("http_server_request_duration", body);
    }

    [Fact]
    public async Task Health_reports_each_dependency()
    {
        var body = await factory.CreateClient().GetStringAsync("/health");
        Assert.Contains("\"name\":\"postgres\"", body);
        Assert.Contains("\"name\":\"redis\"", body);
        Assert.Contains("\"name\":\"outbox\"", body);
    }
}
