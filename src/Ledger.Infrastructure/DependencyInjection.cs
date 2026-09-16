using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Common;
using Ledger.Application.Transactions;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<LedgerDbContext>(o => o
            .UseNpgsql(connectionString, npg => npg
                .MigrationsHistoryTable("__ef_migrations", "ledger")
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<AccountService>();
        services.AddScoped<TransactionService>();
        return services;
    }
}
