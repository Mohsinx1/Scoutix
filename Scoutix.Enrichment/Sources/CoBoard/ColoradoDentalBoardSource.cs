using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;
using Scoutix.Enrichment.Scraping;
using Scoutix.Enrichment.Sources.Npi;

namespace Scoutix.Enrichment.Sources.CoBoard;

/// <summary>
/// The second independent registry: Colorado's dental licensing board (via the state open-data API).
/// The board record carries the licensee's city-of-record (not the practice address), so it can't
/// reliably DISCOVER an owner from a listing alone — instead its job is to CORROBORATE a name another
/// source proposed by confirming an active CO dental license exists for it. Independent agreement
/// here is what turns a resolved name "verified".
///
/// Built as a per-vertical/per-state resolver so HVAC / electrical / salon boards can slot in later.
/// </summary>
public sealed class ColoradoDentalBoardSource : IOwnerNameSource
{
    private readonly CoLicenseClient _client;
    private readonly ILogger<ColoradoDentalBoardSource> _logger;

    public ColoradoDentalBoardSource(HttpClient http, IPageCache cache, ILogger<ColoradoDentalBoardSource> logger, string? appToken = null)
    {
        _client = new CoLicenseClient(http, cache, logger, appToken);
        _logger = logger;
    }

    public string SourceId => EnrichmentSources.StateLicenseBoard;

    // Colorado board → only Colorado listings.
    public bool CanHandle(Listing listing) =>
        !string.IsNullOrWhiteSpace(listing.Name) &&
        string.Equals(NpiMatching.ParseState(listing.City), "CO", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<OwnerNameCandidate>> FindOwnersAsync(OwnerLookupContext context, CancellationToken ct = default)
    {
        var listing = context.Listing;
        if (!CanHandle(listing)) return Array.Empty<OwnerNameCandidate>();

        var best = new Dictionary<string, OwnerNameCandidate>();
        void Consider(string? first, string? last, double conf, string detail)
        {
            var full = NameNormalizer.Clean($"{first} {last}".Trim());
            var key = full is null ? null : NameNormalizer.CanonicalKey(full);
            if (full is null || key is null) return;
            if (!best.TryGetValue(key, out var prev) || conf > prev.Confidence)
                best[key] = new OwnerNameCandidate(full, conf, detail);
        }

        // ---- Corroboration: confirm prior names are active CO-licensed dentists ----
        foreach (var (first, last) in PriorNameHints(context))
        {
            var recs = await _client.SearchDentistsAsync(first, last, null, ct);
            var active = recs.Where(r => r.IsActive).ToList();

            if (active.Count > 0)
            {
                var rec = active[0];
                double conf = active.Count == 1 ? 0.80 : 0.74; // common surname → several DEN matches
                Consider(rec.FirstName, rec.LastName, conf, $"CO DORA DEN active ({rec.City})");
            }
            else if (recs.Count > 0)
            {
                var rec = recs[0];
                Consider(rec.FirstName, rec.LastName, 0.60, $"CO DORA DEN {rec.Status} ({rec.City})");
            }
        }

        // NOTE: no standalone surname-from-business-name discovery here. The board record has no
        // practice phone/address to anchor on (only the licensee's city-of-record), so a location
        // word in the name ("Park Hill Family Dental" → licensee "… Park") matches falsely. Discovery
        // is NPI's job (phone-anchored); the board only corroborates names other sources proposed.

        var results = best.Values.OrderByDescending(c => c.Confidence).ToList();
        _logger.LogInformation("ColoradoDentalBoardSource: listing {Id} ({Name}) -> {Count} candidate(s).",
            listing.Id, listing.Name, results.Count);
        return results;
    }

    private static IEnumerable<(string First, string Last)> PriorNameHints(OwnerLookupContext context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in context.PriorCandidates)
        {
            var clean = NameNormalizer.Clean(c.Name);
            if (clean is null) continue;
            var tokens = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;
            if (seen.Add($"{tokens[0]}|{tokens[^1]}")) yield return (tokens[0], tokens[^1]);
        }
    }
}
