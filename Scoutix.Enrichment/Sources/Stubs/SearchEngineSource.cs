using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Sources.Stubs;

/// <summary>
/// Config-swappable slot for a search-engine lookup ("&lt;business&gt; &lt;city&gt; owner").
/// Conservative posture: we do NOT scrape Google/Bing from the single production IP (captcha + ban
/// risk). This is a clean seam — drop a paid SERP API implementation in here and flip the toggle.
/// Until then it's a no-op so the waterfall composition stays complete.
/// </summary>
public sealed class SearchEngineSource : IOwnerNameSource
{
    public string SourceId => EnrichmentSources.SearchEngine;

    public bool CanHandle(Listing listing) => false; // disabled slot

    public Task<IReadOnlyList<OwnerNameCandidate>> FindOwnersAsync(OwnerLookupContext context, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OwnerNameCandidate>>(Array.Empty<OwnerNameCandidate>());
}
