using Microsoft.EntityFrameworkCore;
using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Data;

/// <summary>
/// Dedicated context for the enrichment harness. Lives in its own database (config key
/// <c>EnrichmentConnection</c>) and never touches the web app's production schema. Tables are
/// created with <c>EnsureCreated()</c> — no migrations folder, this is an isolated R&amp;D store.
/// </summary>
public class EnrichmentDbContext : DbContext
{
    public EnrichmentDbContext(DbContextOptions<EnrichmentDbContext> options) : base(options) { }

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<FieldCandidate> FieldCandidates => Set<FieldCandidate>();
    public DbSet<ListingEnrichment> ListingEnrichments => Set<ListingEnrichment>();
    public DbSet<CachedPage> CachedPages => Set<CachedPage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Listing>(e =>
        {
            e.Property(x => x.SourceKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasMaxLength(400).IsRequired();
            e.Property(x => x.Vertical).HasMaxLength(80);
            e.Property(x => x.City).HasMaxLength(120);
            // Idempotent re-import: one listing per source business.
            e.HasIndex(x => x.SourceKey).IsUnique();
        });

        b.Entity<FieldCandidate>(e =>
        {
            // Persist enums as readable strings so the DB is browsable by hand.
            e.Property(x => x.Field).HasConversion<string>().HasMaxLength(40).IsRequired();
            e.Property(x => x.Source).HasMaxLength(60).IsRequired();
            e.Property(x => x.Value).HasMaxLength(500).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(1000);

            // THE idempotency guarantee: one row per (listing, field, source). Re-runs upsert.
            e.HasIndex(x => new { x.ListingId, x.Field, x.Source }).IsUnique();

            e.HasOne(x => x.Listing)
                .WithMany(l => l.Candidates)
                .HasForeignKey(x => x.ListingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListingEnrichment>(e =>
        {
            e.HasKey(x => x.ListingId);   // 1:1 with Listing
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EmailStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.OwnerName).HasMaxLength(300);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.OwnerNameSources).HasMaxLength(300);
            e.Property(x => x.FailureReason).HasMaxLength(500);

            e.HasOne(x => x.Listing)
                .WithOne(l => l.Enrichment)
                .HasForeignKey<ListingEnrichment>(x => x.ListingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CachedPage>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(800).IsRequired();
            e.HasIndex(x => x.Url).IsUnique();
        });
    }
}
