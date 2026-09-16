using Ledger.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        b.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(256).IsRequired();
        b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.FromAccountId).HasColumnName("from_account_id");
        b.Property(x => x.ToAccountId).HasColumnName("to_account_id");
        b.Property(x => x.RejectionCode).HasColumnName("rejection_code").HasMaxLength(64);
        b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(512);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        // Уникальный индекс — гарантия идемпотентности на уровне БД, а не только кода.
        b.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("ux_transactions_idempotency_key");
        b.HasIndex(x => x.FromAccountId).HasDatabaseName("ix_transactions_from_account");
        b.HasIndex(x => x.ToAccountId).HasDatabaseName("ix_transactions_to_account");

        b.HasMany(x => x.Entries).WithOne().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TransactionId).HasColumnName("transaction_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        b.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasPrecision(19, 4);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        // Монотонный номер выдаёт БД (identity) — это курсор для выписки.
        b.Property(x => x.Sequence).HasColumnName("sequence").ValueGeneratedOnAdd().UseIdentityAlwaysColumn();

        b.HasOne<Ledger.Domain.Accounts.Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        // Составной индекс под запрос выписки: WHERE account_id = ? AND sequence < ? ORDER BY sequence DESC
        b.HasIndex(x => new { x.AccountId, x.Sequence }).HasDatabaseName("ix_ledger_entries_account_sequence");
    }
}
