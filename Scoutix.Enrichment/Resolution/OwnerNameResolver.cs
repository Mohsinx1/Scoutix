using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Resolution;

/// <summary>
/// Collapses every owner-name candidate for a listing into a single answer. A name becomes
/// <c>Verified</c> when two INDEPENDENT sources agree on it (the brief's rule). Pure function —
/// no I/O — so it's cheap to unit-test.
/// </summary>
public static class OwnerNameResolver
{
    private const double AgreementBoostPerSource = 0.15;
    private const double MaxConfidence = 0.99;

    public static ResolvedField Resolve(IEnumerable<FieldCandidate> candidates)
    {
        var names = candidates
            .Where(c => c.Field == EnrichmentField.OwnerName && !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => (Candidate: c, Key: NameNormalizer.CanonicalKey(c.Value)))
            .Where(x => x.Key is not null)
            .ToList();

        if (names.Count == 0)
            return ResolvedField.None;

        // Group candidates that refer to the same person, then pick the best-supported group:
        // most distinct sources first, then highest single-source confidence.
        var best = names
            .GroupBy(x => x.Key)
            .Select(g =>
            {
                var sources = g.Select(x => x.Candidate.Source)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var top = g.OrderByDescending(x => x.Candidate.Confidence).First().Candidate;
                return new { Sources = sources, Top = top, MaxConfidence = g.Max(x => x.Candidate.Confidence) };
            })
            .OrderByDescending(x => x.Sources.Count)
            .ThenByDescending(x => x.MaxConfidence)
            .First();

        var distinctSources = best.Sources.Count;
        var verified = distinctSources >= 2;

        var confidence = verified
            ? Math.Min(MaxConfidence, best.MaxConfidence + AgreementBoostPerSource * (distinctSources - 1))
            : best.MaxConfidence;

        return new ResolvedField
        {
            Value = NameNormalizer.ToDisplayCase(NameNormalizer.Clean(best.Top.Value) ?? best.Top.Value.Trim()),
            Verified = verified,
            Confidence = confidence,
            Sources = best.Sources,
        };
    }
}
