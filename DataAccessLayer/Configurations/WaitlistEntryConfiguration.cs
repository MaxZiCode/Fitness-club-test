using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccessLayer.Configurations;

public class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Seats).HasColumnName("seats");
        builder.Property(x => x.InitialPosition).HasColumnName("initial_position");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.IdempotencyKeyHash)
            .HasColumnName("idempotency_key_hash")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new { x.UserId, x.IdempotencyKeyHash })
            .IsUnique();

        builder.HasIndex(x => new { x.ClassId, x.UserId })
            .IsUnique()
            .HasFilter("status = 'waiting'");

        builder.HasIndex(x => new { x.ClassId, x.CreatedAt, x.Id })
            .HasFilter("status = 'waiting'");

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId);
        builder.HasOne<Class>()
            .WithMany()
            .HasForeignKey(x => x.ClassId);
    }
}
