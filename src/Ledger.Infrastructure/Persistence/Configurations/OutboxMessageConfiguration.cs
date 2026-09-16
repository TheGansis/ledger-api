using Ledger.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        b.Property(x => x.Route).HasColumnName("route").HasMaxLength(128).IsRequired();
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        b.Property(x => x.Attempts).HasColumnName("attempts");
        b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(512);
        // Частичный индекс: публикатор читает только необработанные — индекс маленький даже при миллионах строк.
        b.HasIndex(x => x.Id).HasFilter("processed_at IS NULL").HasDatabaseName("ix_outbox_unprocessed");
    }
}
