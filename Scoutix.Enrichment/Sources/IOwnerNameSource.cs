using Scoutix.Enrichment.Data;

namespace Scoutix.Enrichment.Sources;

/// <summary>
/// One resolver per owner-name source (website, NPI, state board, search, SoS). The orchestrator
/// runs them in order and persists whatever they return — sources never touch the database, which
/// keeps them easy to test and lets the waterfall decide when two sources agree.
/// </summary>
public interface IOwnerNameSource
{
    /// <summary>Stable source id — see <see cref="Model.EnrichmentSources"/>.</summary>
    string SourceId { get; }

    /// <summary>Cheap pre-check so the orchestrator can skip sources that can't help this listing.</summary>
    bool CanHandle(Listing listing);

    /// <summary>Returns zero or more owner-name candidates for the listing.</summary>
    Task<IReadOnlyList<OwnerNameCandidate>> FindOwnersAsync(OwnerLookupContext context, CancellationToken ct = default);
}

/// <summary>A single owner-name guess from a source, with its confidence and a provenance note.</summary>
public sealed record OwnerNameCandidate(string Name, double Confidence, string? Detail = null);

/// <summary>
/// What a source is given: the listing plus whatever earlier sources already proposed. Later
/// sources (NPI, the board) use the prior names to corroborate — confirming a low-confidence website
/// guess against a federal/state registry is how a name reaches "verified".
/// </summary>
public sealed class OwnerLookupContext
{
    public required Listing Listing { get; init; }
    public IReadOnlyList<OwnerNameCandidate> PriorCandidates { get; init; } = Array.Empty<OwnerNameCandidate>();
}
