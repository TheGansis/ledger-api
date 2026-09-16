using System.Net;
using System.Net.Http.Json;
using Ledger.Application.Accounts;
using Ledger.Application.Common;
using Ledger.Application.Transactions;

namespace Ledger.Api.Tests;

[Collection(ApiCollection.Name)]
public class AccountsTests(LedgerApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClient();

    [Fact]
    public async Task Open_account_returns_201_with_location()
    {
        var resp = await _http.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("Alice", "rub"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<AccountDto>())!;
        Assert.Equal($"/api/accounts/{dto.Id}", resp.Headers.Location!.ToString());
        Assert.Equal("RUB", dto.Currency);
        Assert.Equal(0m, dto.Balance);
    }

    [Fact]
    public async Task Open_account_with_invalid_body_returns_400_problem_details()
    {
        var resp = await _http.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("", "RUBL"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"validation\"", body);
        Assert.Contains("OwnerName", body);
        Assert.Contains("Currency", body);
    }

    [Fact]
    public async Task Unknown_account_returns_404()
    {
        var resp = await _http.GetAsync($"/api/accounts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Deposit_and_withdraw_update_balance()
    {
        var acc = await _http.OpenAccountAsync();
        Assert.Equal(HttpStatusCode.Created, (await _http.DepositAsync(acc.Id, 100m, ApiClientExtensions.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _http.WithdrawAsync(acc.Id, 30.25m, ApiClientExtensions.Key())).StatusCode);
        Assert.Equal(69.75m, (await _http.GetAccountAsync(acc.Id)).Balance);
    }

    [Fact]
    public async Task Withdraw_over_balance_is_rejected_transaction_not_error()
    {
        var acc = await _http.OpenAccountAsync();
        await _http.DepositAsync(acc.Id, 10m, ApiClientExtensions.Key());

        var resp = await _http.WithdrawAsync(acc.Id, 11m, ApiClientExtensions.Key());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var t = await resp.AsTransactionAsync();
        Assert.Equal("Rejected", t.Status);
        Assert.Equal("funds.insufficient", t.RejectionCode);
        Assert.Equal(10m, (await _http.GetAccountAsync(acc.Id)).Balance);
    }

    [Fact]
    public async Task Money_operation_without_idempotency_key_returns_400()
    {
        var acc = await _http.OpenAccountAsync();
        var resp = await _http.PostAsJsonAsync($"/api/accounts/{acc.Id}/deposit", new MoneyOperationRequest(1m));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Idempotency-Key", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Amount_with_more_than_four_decimals_is_rejected_by_validation()
    {
        var acc = await _http.OpenAccountAsync();
        var resp = await _http.DepositAsync(acc.Id, 1.00001m, ApiClientExtensions.Key());
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Frozen_account_rejects_deposit_until_unfrozen()
    {
        var acc = await _http.OpenAccountAsync();
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsync($"/api/accounts/{acc.Id}/freeze", null)).StatusCode);

        var t = await (await _http.DepositAsync(acc.Id, 1m, ApiClientExtensions.Key())).AsTransactionAsync();
        Assert.Equal("Rejected", t.Status);
        Assert.Equal("account.inactive", t.RejectionCode);

        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsync($"/api/accounts/{acc.Id}/unfreeze", null)).StatusCode);
        var ok = await (await _http.DepositAsync(acc.Id, 1m, ApiClientExtensions.Key())).AsTransactionAsync();
        Assert.Equal("Completed", ok.Status);
    }

    [Fact]
    public async Task Close_with_non_zero_balance_returns_422()
    {
        var acc = await _http.OpenAccountAsync();
        await _http.DepositAsync(acc.Id, 1m, ApiClientExtensions.Key());
        var resp = await _http.PostAsync($"/api/accounts/{acc.Id}/close", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("account.nonzero_balance", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Statement_is_paginated_by_cursor_newest_first()
    {
        var acc = await _http.OpenAccountAsync();
        for (var i = 1; i <= 5; i++) await _http.DepositAsync(acc.Id, i, ApiClientExtensions.Key());

        var page1 = (await _http.GetFromJsonAsync<Page<LedgerEntryDto>>($"/api/accounts/{acc.Id}/entries?limit=2"))!;
        Assert.Equal([5m, 4m], page1.Items.Select(e => e.Amount));
        Assert.NotNull(page1.NextCursor);

        var page2 = (await _http.GetFromJsonAsync<Page<LedgerEntryDto>>($"/api/accounts/{acc.Id}/entries?limit=2&cursor={page1.NextCursor}"))!;
        Assert.Equal([3m, 2m], page2.Items.Select(e => e.Amount));

        var page3 = (await _http.GetFromJsonAsync<Page<LedgerEntryDto>>($"/api/accounts/{acc.Id}/entries?limit=2&cursor={page2.NextCursor}"))!;
        Assert.Equal([1m], page3.Items.Select(e => e.Amount));
        Assert.Null(page3.NextCursor);

        // Баланс после каждой проводки — накопительный
        Assert.Equal(15m, page1.Items[0].BalanceAfter);
    }
}
