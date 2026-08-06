using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Data;

/// <summary>
/// Append-and-upsert persistence for field candidates. Every source writes through here so the
/// pipeline stays idempotent: the same source re-run updates its own row instead of duplicating.
/// </summary>
public interface ICandidateStore
{
    /// <summary>
    /// Records a candidate for (listing, field, source). Inserts on first sight, updates in place
    /// on re-run — guaranteeing one row per (ListingId, Field, Source).
    /// </summary>
    Task<FieldCandidate> UpsertAsync(
        int listingId,
        EnrichmentField field,
        string value,
        string source,
        double confidence,
        string? detail = null,
        CancellationToken ct = default);

    /// <summary>All candidates recorded for a listing (any field).</summary>
    Task<IReadOnlyList<FieldCandidate>> GetForListingAsync(int listingId, CancellationToken ct = default);
}
