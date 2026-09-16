using System.Net.Http.Json;
using Ledger.Application.Accounts;
using Ledger.Application.Transactions;

namespace Ledger.Api.Tests;

internal static class ApiClientExtensions
{
    public static async Task<AccountDto> OpenAccountAsync(this HttpClient http, string owner = "Test", string currency = "RUB")
    {
        var resp = await http.PostAsJsonAsync("/api/accounts", new OpenAccountRequest(owner, currency));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    public static async Task<AccountDto> GetAccountAsync(this HttpClient http, Guid id) =>
        (await http.GetFromJsonAsync<AccountDto>($"/api/accounts/{id}"))!;

    public static Task<HttpResponseMessage> DepositAsync(this HttpClient http, Guid id, decimal amount, string key) =>
        http.SendMoneyAsync($"/api/accounts/{id}/deposit", new MoneyOperationRequest(amount), key);

    public static Task<HttpResponseMessage> WithdrawAsync(this HttpClient http, Guid id, decimal amount, string key) =>
        http.SendMoneyAsync($"/api/accounts/{id}/withdraw", new MoneyOperationRequest(amount), key);

    public static Task<HttpResponseMessage> TransferAsync(this HttpClient http, Guid from, Guid to, decimal amount, string key) =>
        http.SendMoneyAsync("/api/transactions/transfers", new TransferRequest(from, to, amount), key);

    private static Task<HttpResponseMessage> SendMoneyAsync<T>(this HttpClient http, string url, T body, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return http.SendAsync(req);
    }

    public static async Task<TransactionDto> AsTransactionAsync(this HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<TransactionDto>())!;

    public static string Key() => Guid.NewGuid().ToString("N");
}
