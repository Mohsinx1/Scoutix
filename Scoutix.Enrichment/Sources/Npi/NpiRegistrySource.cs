using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;
using Scoutix.Enrichment.Scraping;

namespace Scoutix.Enrichment.Sources.Npi;

/// <summary>
/// High-precision registry source backed by the federal NPPES NPI Registry. Two ways in:
///   A) the practice ORGANIZATION's authorized official (often the owner), and
///   B) INDIVIDUAL dentists matched from the business name / prior website guesses, confirmed by a
///      phone or city match against the listing.
/// Carries legal provider names that website scraping alone misses — the lift this milestone exists to measure.
/// </summary>
public sealed class NpiRegistrySource : IOwnerNameSource
{
    private enum LocMatch { None, City, Phone }

    private readonly NpiClient _client;
    private readonly ILogger<NpiRegistrySource> _logger;

    public NpiRegistrySource(HttpClient http, IPageCache cache, ILogger<NpiRegistrySource> logger)
    {
        _client = new NpiClient(http, cache, logger);
        _logger = logger;
    }

    public string SourceId => EnrichmentSources.Npi;

    public bool CanHandle(Listing listing) =>
        !string.IsNullOrWhiteSpace(listing.Name) && NpiMatching.ParseState(listing.City) is not null;

    public async Task<IReadOnlyList<OwnerNameCandidate>> FindOwnersAsync(OwnerLookupContext context, CancellationToken ct = default)
    {
        var listing = context.Listing;
        var state = NpiMatching.ParseState(listing.City);
        if (state is null) return Array.Empty<OwnerNameCandidate>();

        var city = NpiMatching.ParseCity(listing.City);
        var listingPhone = NpiMatching.DigitsOnly(listing.Phone);

        var best = new Dictionary<string, OwnerNameCandidate>();
        void Consider(string? first, string? last, double conf, string detail)
        {
            var full = NameNormalizer.Clean($"{first} {last}".Trim());
            var key = full is null ? null : NameNormalizer.CanonicalKey(full);
            if (full is null || key is null) return;
            if (!best.TryGetValue(key, out var prev) || conf > prev.Confidence)
                best[key] = new OwnerNameCandidate(full, conf, detail);
        }

        // ---- Mode A: organization authorized official ----
        var orgName = NpiMatching.CleanOrgName(listing.Name);
        if (orgName.Length >= 3)
        {
            var orgs = await _client.SearchAsync(new NpiQuery
            {
                EnumerationType = "NPI-2",
                OrganizationName = orgName + "*",
                State = state,
                City = city,
                Limit = 10,
            }, ct);

            foreach (var org in orgs)
            {
                var b = org.Basic;
                if (string.IsNullOrWhiteSpace(b?.AuthorizedOfficialLastName)) continue;

                var loc = LocationOf(org, city, listingPhone);
                if (loc == LocMatch.None && orgs.Count > 1) continue; // ambiguous org — need a location anchor

                double conf = loc switch { LocMatch.Phone => 0.88, LocMatch.City => 0.82, _ => 0.70 };
                Consider(b.AuthorizedOfficialFirstName, b.AuthorizedOfficialLastName, conf,
                    $"NPI-2 {org.Number} authorized official ({b.AuthorizedOfficialTitle})");
            }
        }

        // ---- Mode B: individual dentists from prior names + business-name surnames ----
        foreach (var hint in BuildNameHints(context, listing))
        {
            var inds = (await _client.SearchAsync(new NpiQuery
            {
                EnumerationType = "NPI-1",
                FirstName = hint.First,
                LastName = hint.Last,
                TaxonomyDescription = "Dentist",
                State = state,
                Limit = 20,
            }, ct)).Where(IsDentist).ToList();

            foreach (var ind in inds)
            {
                var loc = LocationOf(ind, city, listingPhone);

                // A surname-only guess (no first name) is only trustworthy with a phone match.
                if (hint.First is null && loc != LocMatch.Phone) continue;
                // A full-name guess with no location anchor is only trusted when the name is rare in-state.
                if (hint.First is not null && loc == LocMatch.None && inds.Count > 3) continue;

                double conf = loc switch { LocMatch.Phone => 0.90, LocMatch.City => 0.80, _ => 0.60 };
                Consider(ind.Basic?.FirstName, ind.Basic?.LastName, conf, $"NPI-1 {ind.Number}");
            }
        }

        var results = best.Values.OrderByDescending(c => c.Confidence).ToList();
        _logger.LogInformation("NpiRegistrySource: listing {Id} ({Name}) -> {Count} candidate(s).",
            listing.Id, listing.Name, results.Count);
        return results;
    }

    private static IEnumerable<(string? First, string Last)> BuildNameHints(OwnerLookupContext context, Listing listing)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Prior website/search guesses → first + last.
        foreach (var c in context.PriorCandidates)
        {
            var clean = NameNormalizer.Clean(c.Name);
            if (clean is null) continue;
            var tokens = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;
            if (seen.Add($"{tokens[0]}|{tokens[^1]}")) yield return (tokens[0], tokens[^1]);
        }

        // Surnames embedded in the business name ("Miller Family Dental" → Miller).
        foreach (var surname in NpiMatching.DeriveSurnames(listing.Name))
            if (seen.Add($"|{surname}")) yield return (null, surname);
    }

    private static bool IsDentist(NpiResult r) =>
        r.Taxonomies is null || r.Taxonomies.Count == 0 ||
        r.Taxonomies.Any(t => t.Desc is not null && t.Desc.Contains("dent", StringComparison.OrdinalIgnoreCase));

    private static LocMatch LocationOf(NpiResult r, string? city, string? listingPhone)
    {
        if (r.Addresses is null) return LocMatch.None;

        if (listingPhone is not null &&
            r.Addresses.Any(a => NpiMatching.DigitsOnly(a.TelephoneNumber) == listingPhone))
            return LocMatch.Phone;

        if (!string.IsNullOrWhiteSpace(city) &&
            r.Addresses.Any(a => string.Equals(a.City?.Trim(), city, StringComparison.OrdinalIgnoreCase)))
            return LocMatch.City;

        return LocMatch.None;
    }
}
