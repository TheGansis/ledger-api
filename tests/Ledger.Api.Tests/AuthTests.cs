using System.Net;
using System.Net.Http.Json;
using Ledger.Application.Accounts;
using Ledger.Application.Transactions;

namespace Ledger.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(LedgerApiFactory factory)
{
    [Fact]
    public async Task Without_token_returns_401()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("X", "RUB"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/accounts/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Forged_token_is_rejected()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJoYWNrZXIifQ.invalidsignature");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"/api/accounts/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Account_is_bound_to_token_subject()
    {
        var alice = factory.CreateClientFor("alice");
        var acc = await alice.OpenAccountAsync("Alice");
        Assert.Equal("alice", acc.OwnerId);
    }

    [Fact]
    public async Task Other_user_cannot_read_withdraw_or_view_statement_but_can_transfer_in()
    {
        var alice = factory.CreateClientFor("alice-" + Guid.NewGuid());
        var bob = factory.CreateClientFor("bob-" + Guid.NewGuid());
        var a = await alice.OpenAccountAsync("Alice");
        var b = await bob.OpenAccountAsync("Bob");
        await bob.DepositAsync(b.Id, 100m, ApiClientExtensions.Key());

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/accounts/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/accounts/{a.Id}/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.WithdrawAsync(a.Id, 1m, ApiClientExtensions.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DepositAsync(a.Id, 1m, ApiClientExtensions.Key())).StatusCode);
        // Перевод со своего на чужой — разрешён; с чужого — нет.
        Assert.Equal(HttpStatusCode.Created, (await bob.TransferAsync(b.Id, a.Id, 10m, ApiClientExtensions.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.TransferAsync(a.Id, b.Id, 1m, ApiClientExtensions.Key())).StatusCode);
        Assert.Equal(10m, (await alice.GetAccountAsync(a.Id)).Balance);
    }

    [Fact]
    public async Task Forbidden_transfer_does_not_leave_transaction_or_consume_idempotency_key()
    {
        var alice = factory.CreateClientFor("alice-" + Guid.NewGuid());
        var bob = factory.CreateClientFor("bob-" + Guid.NewGuid());
        var a = await alice.OpenAccountAsync("Alice");
        var b = await bob.OpenAccountAsync("Bob");
        await alice.DepositAsync(a.Id, 50m, ApiClientExtensions.Key());
        var key = ApiClientExtensions.Key();

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.TransferAsync(a.Id, b.Id, 5m, key)).StatusCode);
        // Владелец может использовать тот же ключ — попытка чужого ничего не записала.
        Assert.Equal(HttpStatusCode.Created, (await alice.TransferAsync(a.Id, b.Id, 5m, key)).StatusCode);
    }

    [Fact]
    public async Task Transaction_is_visible_to_both_participants_only()
    {
        var alice = factory.CreateClientFor("alice-" + Guid.NewGuid());
        var bob = factory.CreateClientFor("bob-" + Guid.NewGuid());
        var eve = factory.CreateClientFor("eve-" + Guid.NewGuid());
        var a = await alice.OpenAccountAsync("Alice");
        var b = await bob.OpenAccountAsync("Bob");
        await alice.DepositAsync(a.Id, 10m, ApiClientExtensions.Key());
        var t = await (await alice.TransferAsync(a.Id, b.Id, 3m, ApiClientExtensions.Key())).AsTransactionAsync();

        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/transactions/{t.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/transactions/{t.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await eve.GetAsync($"/api/transactions/{t.Id}")).StatusCode);
    }

    [Fact]
    public async Task Status_changes_require_admin_role()
    {
        var alice = factory.CreateClientFor("alice-" + Guid.NewGuid());
        var admin = factory.CreateClientFor("admin-1", "admin");
        var a = await alice.OpenAccountAsync("Alice");

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync($"/api/accounts/{a.Id}/freeze", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/accounts/{a.Id}/freeze", null)).StatusCode);
        Assert.Equal("Frozen", (await alice.GetAccountAsync(a.Id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/accounts/{a.Id}")).StatusCode); // админ видит любой счёт
    }

    [Fact]
    public async Task Admin_can_open_account_for_customer_but_customer_cannot_for_others()
    {
        var admin = factory.CreateClientFor("admin-1", "admin");
        var alice = factory.CreateClientFor("alice-" + Guid.NewGuid());

        var resp = await admin.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("Carol", "RUB", "carol"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal("carol", (await resp.Content.ReadFromJsonAsync<AccountDto>())!.OwnerId);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("Dave", "RUB", "dave"))).StatusCode);
    }
}
