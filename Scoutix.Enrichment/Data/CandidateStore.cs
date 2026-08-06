using Microsoft.EntityFrameworkCore;
using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Data;

public class CandidateStore : ICandidateStore
{
    private readonly EnrichmentDbContext _db;

    public CandidateStore(EnrichmentDbContext db) => _db = db;

    public async Task<FieldCandidate> UpsertAsync(
        int listingId,
        EnrichmentField field,
        string value,
        string source,
        double confidence,
        string? detail = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Candidate value cannot be blank.", nameof(value));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Candidate source cannot be blank.", nameof(source));

        var existing = await _db.FieldCandidates
            .FirstOrDefaultAsync(
                c => c.ListingId == listingId && c.Field == field && c.Source == source,
                ct);

        if (existing is null)
        {
            existing = new FieldCandidate
            {
                ListingId = listingId,
                Field = field,
                Value = value.Trim(),
                Source = source,
                Confidence = confidence,
                Detail = detail,
                ObservedAt = DateTime.UtcNow,
            };
            _db.FieldCandidates.Add(existing);
        }
        else
        {
            // Same source, seen again — refresh the value/confidence/provenance in place.
            existing.Value = value.Trim();
            existing.Confidence = confidence;
            existing.Detail = detail;
            existing.ObservedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<IReadOnlyList<FieldCandidate>> GetForListingAsync(int listingId, CancellationToken ct = default)
    {
        return await _db.FieldCandidates
            .Where(c => c.ListingId == listingId)
            .OrderBy(c => c.Field)
            .ThenByDescending(c => c.Confidence)
            .ToListAsync(ct);
    }
}
