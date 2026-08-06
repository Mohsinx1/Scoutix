namespace Scoutix.Enrichment.Data;

/// <summary>
/// Persistence for listings and their per-listing enrichment rollup. Listing upsert is keyed on
/// <see cref="Listing.SourceKey"/> so re-importing the same set never duplicates listings.
/// </summary>
public interface IListingStore
{
    Task<Listing> UpsertListingAsync(Listing listing, CancellationToken ct = default);
    Task<Listing?> GetListingAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Listing>> GetListingsAsync(string vertical, string city, CancellationToken ct = default);
    /// <summary>Listings whose enrichment is missing or older than the cutoff (for re-verification).</summary>
    Task<IReadOnlyList<Listing>> GetListingsNeedingReVerificationAsync(DateTime cutoffUtc, CancellationToken ct = default);
    Task<ListingEnrichment?> GetEnrichmentAsync(int listingId, CancellationToken ct = default);
    Task UpsertEnrichmentAsync(ListingEnrichment rollup, CancellationToken ct = default);
}
