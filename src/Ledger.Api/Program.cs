using FluentValidation;
using Ledger.Api.Endpoints;
using Ledger.Api.Middleware;
using Ledger.Application.Accounts;
using Ledger.Infrastructure;
using Ledger.Infrastructure.Outbox;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLedgerInfrastructure(builder.Configuration);

builder.Services.AddValidatorsFromAssemblyContaining<OpenAccountValidator>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsMapping>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Ledger API", Version = "v1",
        Description = "Счета и переводы с двойной записью, идемпотентностью и блокировками строк." });
});
builder.Services.AddHealthChecks()
    .AddDbContextCheck<LedgerDbContext>("postgres")
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", "redis")
    .AddCheck<OutboxHealthCheck>("outbox");

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health", new()
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), e.Value.Description, e.Value.Data }),
        });
    },
});
app.MapAccounts();
app.MapTransactions();

// Миграции применяются при старте: для dev/tests удобно, для прода — отдельный шаг в CI (см. README).
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
}

app.Run();

public partial class Program;
