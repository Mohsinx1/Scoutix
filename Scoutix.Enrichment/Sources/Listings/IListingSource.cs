using Scoutix.Enrichment.Data;

namespace Scoutix.Enrichment.Sources.Listings;

/// <summary>
/// Supplies the businesses to enrich. M1 reads a CSV (which doubles as the apples-to-apples set for
/// the Outscraper comparison); a Maps-scrape implementation can slot in behind the same interface.
/// </summary>
public interface IListingSource
{
    Task<IReadOnlyList<Listing>> ReadAsync(CancellationToken ct = default);
}
