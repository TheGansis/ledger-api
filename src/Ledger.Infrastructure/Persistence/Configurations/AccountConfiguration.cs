using Ledger.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OwnerName).HasColumnName("owner_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Balance).HasColumnName("balance").HasPrecision(19, 4);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        // xmin — системный столбец PostgreSQL, меняется при каждом UPDATE строки.
        // EF сравнивает его в WHERE при сохранении: если строка изменилась — DbUpdateConcurrencyException.
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        // Баланс не может уйти в минус даже при ошибке в коде — последняя линия обороны на уровне БД.
        b.ToTable(t => t.HasCheckConstraint("ck_accounts_balance_non_negative", "balance >= 0"));
    }
}
