using System.Text;
using FluentValidation;
using Ledger.Api.Auth;
using Ledger.Api.Endpoints;
using Ledger.Api.Middleware;
using Ledger.Api.Observability;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Infrastructure;
using Ledger.Infrastructure.Outbox;
using Ledger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Formatting.Compact;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog: структурные логи (JSON в проде, читаемые в dev), traceId/spanId из Activity, пользователь из claims.
    // preserveStaticLogger: логгер принадлежит хосту, а не статике — в тестах поднимается несколько хостов в одном процессе.
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .Enrich.FromLogContext()
           .Enrich.WithProperty("service", "ledger-api");
        if (ctx.HostingEnvironment.IsDevelopment() || ctx.HostingEnvironment.IsEnvironment("Testing"))
            cfg.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        else
            cfg.WriteTo.Console(new CompactJsonFormatter());
    }, preserveStaticLogger: true);

    builder.Services.AddLedgerInfrastructure(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

    // JWT Bearer. Ключ обязателен — приложение не стартует с пустым.
    var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
    if (jwt.SigningKey.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.MapInboundClaims = true; // "sub" → ClaimTypes.NameIdentifier, "role" → ClaimTypes.Role
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));
    builder.Services.AddLedgerRateLimiting(builder.Configuration);

    builder.Services.AddValidatorsFromAssemblyContaining<OpenAccountValidator>();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ProblemDetailsMapping>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o =>
    {
        o.SwaggerDoc("v1", new() { Title = "Ledger API", Version = "v1",
            Description = "Счета и переводы с двойной записью, идемпотентностью и блокировками строк." });
        o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
            Description = "Получите токен в POST /api/auth/dev-token и вставьте сюда." });
        o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    });
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<LedgerDbContext>("postgres")
        .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", "redis")
        .AddCheck<OutboxHealthCheck>("outbox");
    builder.Services.AddLedgerTelemetry(builder.Configuration, "ledger-api");

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging(o =>
    {
        // Middleware по умолчанию пишет в статический Log.Logger; при preserveStaticLogger он пуст — берём логгер хоста.
        o.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        o.EnrichDiagnosticContext = (d, http) =>
        {
            d.Set("UserId", http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null);
            d.Set("IdempotencyKey", http.Request.Headers[IdempotencyKey.Header].ToString());
        };
    });
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapPrometheusScrapingEndpoint("/metrics");
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
    if (jwt.DevTokenEndpoint) app.MapDevTokens();
    app.MapAccounts();
    app.MapTransactions();

    // Миграции применяются при старте: для dev/tests удобно, для прода — отдельный шаг в CI (см. README).
    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Console.Error.WriteLine($"Ledger API terminated unexpectedly: {ex}");
    throw;
}

public partial class Program;
