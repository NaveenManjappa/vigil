using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Cases;
using Vigil.Infrastructure.Events;
using Vigil.Infrastructure.Fraud;
using Vigil.Infrastructure.Workflow;

namespace Vigil.Infrastructure;

public sealed class VigilDbContext(DbContextOptions<VigilDbContext> options) : DbContext(options)
{
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<StoredAlert> Alerts => Set<StoredAlert>();
    public DbSet<StoredCase> Cases => Set<StoredCase>();
    public DbSet<StoredConfirmedFraud> ConfirmedFrauds => Set<StoredConfirmedFraud>();
    public DbSet<StoredCheckpoint> Checkpoints => Set<StoredCheckpoint>();

    /// <summary>
    /// Serialises detection for one Account across concurrent requests, so two ingests cannot both
    /// see "no Alert yet" and open one each. Transaction-scoped, so commit or rollback always releases it.
    /// </summary>
    public Task LockAccountAsync(string accountId, CancellationToken ct = default) =>
        Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({accountId}, 0))", ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAlerts(modelBuilder);

        var checkpoints = modelBuilder.Entity<StoredCheckpoint>();
        checkpoints.ToTable("workflow_checkpoints");
        checkpoints.HasKey(c => c.Position);
        checkpoints.Property(c => c.Position).UseIdentityAlwaysColumn();
        checkpoints.Property(c => c.SessionId).HasMaxLength(64);
        checkpoints.Property(c => c.CheckpointId).HasMaxLength(64);
        checkpoints.Property(c => c.ParentId).HasMaxLength(64);
        checkpoints.HasIndex(c => new { c.SessionId, c.CheckpointId }).IsUnique();
        // json, not jsonb: jsonb reorders keys, and System.Text.Json reads polymorphic payloads only when their
        // type marker comes first.
        checkpoints.Property(c => c.Payload).HasColumnType("json");

        var frauds = modelBuilder.Entity<StoredConfirmedFraud>();
        frauds.ToTable("confirmed_frauds");
        frauds.HasKey(f => f.AccountId);
        frauds.Property(f => f.AccountId).HasMaxLength(64);
        frauds.Property(f => f.Source).HasConversion<string>().HasMaxLength(32);

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
                StringMapComparer);
    }

    // Compares by content, so EF notices an entry changed inside the same dictionary instance. Null-safe because a
    // Case has no pseudonym map until its Investigation runs.
    private static readonly ValueComparer<Dictionary<string, string>?> StringMapComparer = new(
        (a, b) => a == null ? b == null : b != null && a.Count == b.Count && !a.Except(b).Any(),
        v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
        v => v == null ? null : new Dictionary<string, string>(v));

    private static void ConfigureAlerts(ModelBuilder modelBuilder)
    {
        var alerts = modelBuilder.Entity<StoredAlert>();
        alerts.ToTable("alerts");
        alerts.HasKey(a => a.AlertId);
        alerts.HasIndex(a => a.AccountId);
        alerts.Property(a => a.AccountId).HasMaxLength(64);
        alerts.Property(a => a.Band).HasConversion<string>().HasMaxLength(16);
        alerts.Property(a => a.RulesVersion).HasMaxLength(32);
        alerts.HasMany(a => a.Hits).WithOne().HasForeignKey(h => h.AlertId);

        var hits = modelBuilder.Entity<StoredRuleHit>();
        hits.ToTable("rule_hits");
        hits.HasKey(h => h.Id);
        hits.Property(h => h.RuleId).HasMaxLength(16);

        // Native uuid[] rather than a join table: evidence is always read whole with its hit, never queried alone.
        hits.Property(h => h.EvidenceEventIds).HasColumnType("uuid[]");

        var cases = modelBuilder.Entity<StoredCase>();
        cases.ToTable("cases");
        cases.HasKey(c => c.CaseId);
        cases.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        cases.Property(c => c.PromptVersion).HasMaxLength(64);

        // Kept as raw JSON here: the Finding's shape belongs to Vigil.Agents, which Infrastructure does not reference.
        cases.Property(c => c.Finding).HasColumnType("jsonb");
        cases.Property(c => c.EvidenceRefs)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, List<Guid>>>(v, JsonSerializerOptions.Default),
                new ValueComparer<Dictionary<string, List<Guid>>?>(
                    (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
                    v => v == null ? null : v.ToDictionary(e => e.Key, e => e.Value.ToList())));
        cases.Property(c => c.PseudonymMap)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default),
                StringMapComparer);

        // The worker's startup re-queue and the daily limit both filter on these.
        cases.HasIndex(c => c.Status);
        cases.HasIndex(c => c.InvestigationStartedAt);

        // One-to-one with a unique AlertId, so the database itself refuses a second Case for an Alert.
        alerts.HasOne(a => a.Case).WithOne(c => c.Alert).HasForeignKey<StoredCase>(c => c.AlertId);
    }
}
