using System.Net.Http.Json;
using Ledger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.Tests;

/// <summary>
/// Поднимает API in-memory поверх настоящей PostgreSQL (база ledger_test): блокировки строк,
/// уникальные индексы и check-constraint'ы на SQLite не воспроизводятся, поэтому мокать БД
/// здесь — значит не тестировать главное. Схема пересоздаётся один раз на прогон.
/// </summary>
public class LedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ConnectionString = "Host=localhost;Port=5432;Database=ledger_test;Username=ledger;Password=ledger";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ledger", Environment.GetEnvironmentVariable("LEDGER_TEST_DB") ?? ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Jwt:DevTokenEndpoint", "true");
        builder.UseSetting("RateLimiting:MoneyOpsPerMinute", "100000"); // тесты конкурентности шлют сотни запросов
    }

    /// <summary>HttpClient с JWT для указанного пользователя. Каждый тест — свой subject, чтобы тесты не видели чужих счетов.</summary>
    public HttpClient CreateClientFor(string subject, params string[] roles)
    {
        var http = CreateClient();
        var resp = http.PostAsJsonAsync("/api/auth/dev-token", new { subject, roles = roles.Length == 0 ? null : roles }).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        var token = resp.Content.ReadFromJsonAsync<DevTokenResponse>().GetAwaiter().GetResult()!.AccessToken;
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return http;
    }

    private sealed record DevTokenResponse(string AccessToken);

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaReady;

    public async Task InitializeAsync()
    {
        // Схема пересоздаётся один раз на процесс: фабрик может быть несколько (см. RateLimitTests), базa одна.
        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaReady) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            // Сносим схему, а не базу: тестовой роли не нужны права CREATEDB, и это быстрее.
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS ledger CASCADE");
            await db.Database.MigrateAsync();
            _schemaReady = true;
        }
        finally { SchemaLock.Release(); }
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LedgerApiFactory>
{
    public const string Name = "api";
}
