using FluentValidation;
using Ledger.Api.Endpoints;
using Ledger.Api.Middleware;
using Ledger.Application.Accounts;
using Ledger.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLedgerInfrastructure(
    builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException("ConnectionStrings:Ledger is not configured."));

builder.Services.AddValidatorsFromAssemblyContaining<OpenAccountValidator>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsMapping>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Ledger API", Version = "v1",
        Description = "Счета и переводы с двойной записью, идемпотентностью и блокировками строк." });
});
builder.Services.AddHealthChecks().AddDbContextCheck<LedgerDbContext>("postgres");

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
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
