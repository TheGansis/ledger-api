using Ledger.Domain.Accounts;
using Ledger.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ledger");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }
}
