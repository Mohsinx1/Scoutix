using Microsoft.EntityFrameworkCore;

namespace Scoutix.Enrichment.Data;

public sealed class ListingStore : IListingStore
{
    private readonly EnrichmentDbContext _db;

    public ListingStore(EnrichmentDbContext db) => _db = db;

    public async Task<Listing> UpsertListingAsync(Listing listing, CancellationToken ct = default)
    {
        var existing = await _db.Listings.FirstOrDefaultAsync(l => l.SourceKey == listing.SourceKey, ct);
        if (existing is null)
        {
            listing.CreatedAt = listing.CreatedAt == default ? DateTime.UtcNow : listing.CreatedAt;
            _db.Listings.Add(listing);
            await _db.SaveChangesAsync(ct);
            return listing;
        }

        // Refresh the mutable fields from the latest import.
        existing.Name = listing.Name;
        existing.Phone = listing.Phone;
        existing.Email = listing.Email;
        existing.Website = listing.Website;
        existing.Address = listing.Address;
        existing.Vertical = listing.Vertical;
        existing.City = listing.City;
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<Listing?> GetListingAsync(int id, CancellationToken ct = default) =>
        await _db.Listings.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<Listing>> GetListingsAsync(string vertical, string city, CancellationToken ct = default) =>
        await _db.Listings
            .Where(l => l.Vertical == vertical && l.City == city)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Listing>> GetListingsNeedingReVerificationAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        await _db.Listings
            .Where(l => !_db.ListingEnrichments.Any(e => e.ListingId == l.Id && e.UpdatedAt >= cutoffUtc))
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

    public async Task<ListingEnrichment?> GetEnrichmentAsync(int listingId, CancellationToken ct = default) =>
        await _db.ListingEnrichments.FirstOrDefaultAsync(e => e.ListingId == listingId, ct);

    public async Task UpsertEnrichmentAsync(ListingEnrichment rollup, CancellationToken ct = default)
    {
        var existing = await _db.ListingEnrichments.FirstOrDefaultAsync(e => e.ListingId == rollup.ListingId, ct);
        if (existing is null)
        {
            _db.ListingEnrichments.Add(rollup);
        }
        else
        {
            existing.OwnerName = rollup.OwnerName;
            existing.OwnerNameVerified = rollup.OwnerNameVerified;
            existing.OwnerNameConfidence = rollup.OwnerNameConfidence;
            existing.OwnerNameSources = rollup.OwnerNameSources;
            existing.Email = rollup.Email;
            existing.EmailStatus = rollup.EmailStatus;
            existing.EmailConfidence = rollup.EmailConfidence;
            existing.Status = rollup.Status;
            existing.FailureReason = rollup.FailureReason;
            existing.UpdatedAt = rollup.UpdatedAt;
        }
        await _db.SaveChangesAsync(ct);
    }
}
