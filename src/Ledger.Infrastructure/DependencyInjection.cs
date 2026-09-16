using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Common;
using Ledger.Application.Transactions;
using Ledger.Infrastructure.Caching;
using Ledger.Infrastructure.Outbox;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Ledger")
            ?? throw new InvalidOperationException("ConnectionStrings:Ledger is not configured.");

        services.AddDbContext<LedgerDbContext>(o => o
            .UseNpgsql(connectionString, npg => npg
                .MigrationsHistoryTable("__ef_migrations", "ledger")
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();

        // Outbox → RabbitMQ
        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddScoped<IOutbox, EfOutbox>();
        if (config.GetValue("Outbox:PublisherEnabled", true))
            services.AddHostedService<OutboxPublisher>();

        // Redis cache-aside
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = config.GetConnectionString("Redis") ?? "localhost:6379";
            o.InstanceName = "ledger:";
        });
        services.AddScoped<IAccountCache, RedisAccountCache>();

        services.AddScoped<AccountService>();
        services.AddScoped<TransactionService>();
        return services;
    }
}
