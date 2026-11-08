using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Agents;
using Vigil.Domain.Detection;

namespace Vigil.Api.Data;

/// <summary>Three tables: events, alerts and cases. Lists and the Finding are stored as JSON columns.</summary>
public sealed class VigilDbContext(DbContextOptions<VigilDbContext> options) : DbContext(options)
{
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Case> Cases => Set<Case>();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var events = modelBuilder.Entity<StoredEvent>();
        events.ToTable("events");
        events.HasKey(e => e.EventId);
        // Every rule reads one Account's timeline, so this is the hot path.
        events.HasIndex(e => new { e.AccountId, e.OccurredAt });
        // Stored as text so the table stays readable in psql and a new event type needs no migration.
        events.Property(e => e.Type).HasConversion<string>().HasMaxLength(64);
        AsJson(events.Property(e => e.Attributes));

        var alerts = modelBuilder.Entity<Alert>();
        alerts.ToTable("alerts");
        alerts.HasKey(a => a.Id);
        alerts.HasIndex(a => a.AccountId);
        alerts.Property(a => a.Band).HasConversion<string>().HasMaxLength(16);
        AsJson(alerts.Property(a => a.Hits));

        var cases = modelBuilder.Entity<Case>();
        cases.ToTable("cases");
        cases.HasKey(c => c.Id);
        // A unique AlertId, so the database itself refuses a second Case for one Alert.
        cases.HasOne(c => c.Alert).WithOne().HasForeignKey<Case>(c => c.AlertId);
        cases.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        cases.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(32);
        cases.Property(c => c.Action).HasConversion<string>().HasMaxLength(32);
        cases.Property(c => c.Intervention).HasConversion<string>().HasMaxLength(32);
        AsJson(cases.Property(c => c.Finding));
    }

    // Compared by content, so EF notices a list that changed in place, such as an Alert's hits after Add.
    private static void AsJson<T>(PropertyBuilder<T> property) =>
        property.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<T>(v, Json)!,
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!));
}
