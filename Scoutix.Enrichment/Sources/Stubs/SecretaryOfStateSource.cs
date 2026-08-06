using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Sources.Stubs;

/// <summary>
/// Config-swappable slot for Secretary-of-State / OpenCorporates officer lookup. Conservative
/// posture: no raw SoS-site scraping from the production IP. Drop an OpenCorporates (or per-state SoS
/// API) implementation in here and flip the toggle. Per the brief, results that look like a
/// registered-agent service should be flagged low-confidence when this is implemented.
/// No-op for now so the waterfall composition stays complete.
/// </summary>
public sealed class SecretaryOfStateSource : IOwnerNameSource
{
    public string SourceId => EnrichmentSources.SecretaryOfState;

    public bool CanHandle(Listing listing) => false; // disabled slot

    public Task<IReadOnlyList<OwnerNameCandidate>> FindOwnersAsync(OwnerLookupContext context, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OwnerNameCandidate>>(Array.Empty<OwnerNameCandidate>());
}
