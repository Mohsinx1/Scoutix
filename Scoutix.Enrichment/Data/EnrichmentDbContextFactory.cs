using Microsoft.EntityFrameworkCore;

namespace Scoutix.Enrichment.Data;

/// <summary>
/// Builds a SqlServer-backed <see cref="EnrichmentDbContext"/> from a connection string and ensures
/// the schema exists. Used by the console runner; tests construct the context directly with the
/// in-memory provider instead.
/// </summary>
public static class EnrichmentDbContextFactory
{
    public static DbContextOptions<EnrichmentDbContext> BuildOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<EnrichmentDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public static EnrichmentDbContext Create(string connectionString)
        => new(BuildOptions(connectionString));

    /// <summary>Creates the context and makes sure its tables exist (no migrations — R&amp;D store).</summary>
    public static async Task<EnrichmentDbContext> CreateAndEnsureCreatedAsync(
        string connectionString, CancellationToken ct = default)
    {
        var ctx = Create(connectionString);
        await ctx.Database.EnsureCreatedAsync(ct);
        return ctx;
    }
}
