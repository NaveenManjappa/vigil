using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Vigil.Infrastructure.Events;

namespace Vigil.Infrastructure;

public sealed class VigilDbContext(DbContextOptions<VigilDbContext> options) : DbContext(options)
{
    public DbSet<StoredEvent> Events => Set<StoredEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var events = modelBuilder.Entity<StoredEvent>();
        events.ToTable("events");
        events.HasKey(e => e.EventId);

        // Rules look back over one account's recent history, so this is the hot path (ADR-0010).
        events.HasIndex(e => new { e.AccountId, e.OccurredAt });

        // Stored as text so the table stays readable in psql and new event types need no migration.
        events.Property(e => e.Type).HasConversion<string>().HasMaxLength(64);

        events.Property(e => e.AccountId).HasMaxLength(64);
        events.Property(e => e.HolderId).HasMaxLength(64);
        events.Property(e => e.DeviceId).HasMaxLength(128);
        events.Property(e => e.IpAddress).HasMaxLength(64);
        events.Property(e => e.Country).HasMaxLength(2);
        events.Property(e => e.Channel).HasMaxLength(32);

        events.Property(e => e.Attributes)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                    v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)));
    }
}
