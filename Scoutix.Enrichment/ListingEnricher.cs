using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Email;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;
using Scoutix.Enrichment.Sources;

namespace Scoutix.Enrichment;

/// <summary>
/// The waterfall orchestrator for one listing. Runs owner-name sources in order, feeding each the
/// names earlier sources produced (so registries can corroborate the website's guess). Every
/// candidate is persisted idempotently; the listing's owner is re-resolved after each source and the
/// run stops early the moment two independent sources agree. Writes a per-listing rollup.
/// Email enrichment (Stage 2) plugs into the same flow in Step 7.
/// </summary>
public sealed class ListingEnricher
{
    private readonly IReadOnlyList<IOwnerNameSource> _ownerSources;
    private readonly ICandidateStore _candidates;
    private readonly IListingStore _listings;
    private readonly IEmailStage? _emailStage;
    private readonly ILogger<ListingEnricher> _logger;

    public ListingEnricher(
        IReadOnlyList<IOwnerNameSource> ownerSources,
        ICandidateStore candidates,
        IListingStore listings,
        ILogger<ListingEnricher> logger,
        IEmailStage? emailStage = null)
    {
        _ownerSources = ownerSources;
        _candidates = candidates;
        _listings = listings;
        _emailStage = emailStage;
        _logger = logger;
    }

    public async Task<ListingEnrichment> EnrichAsync(Listing listing, bool forceReRun = false, CancellationToken ct = default)
    {
        // Resumability: start from whatever a previous run already recorded. A forced run
        // (re-verification) re-hits the sources even if the listing already verified.
        var candidates = await _candidates.GetForListingAsync(listing.Id, ct);
        var resolved = OwnerNameResolver.Resolve(candidates);

        if (forceReRun || !resolved.Verified)
        {
            foreach (var source in _ownerSources)
            {
                if (ct.IsCancellationRequested) break;
                if (!source.CanHandle(listing)) continue;

                var prior = candidates
                    .Where(c => c.Field == EnrichmentField.OwnerName)
                    .Select(c => new OwnerNameCandidate(c.Value, c.Confidence, c.Detail))
                    .ToList();

                IReadOnlyList<OwnerNameCandidate> found;
                try
                {
                    found = await source.FindOwnersAsync(
                        new OwnerLookupContext { Listing = listing, PriorCandidates = prior }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Source {Source} threw for listing {Id}.", source.SourceId, listing.Id);
                    continue;
                }

                foreach (var c in found)
                    await _candidates.UpsertAsync(
                        listing.Id, EnrichmentField.OwnerName, c.Name, source.SourceId, c.Confidence, c.Detail, ct);

                candidates = await _candidates.GetForListingAsync(listing.Id, ct);
                resolved = OwnerNameResolver.Resolve(candidates);

                if (resolved.Verified)
                {
                    _logger.LogInformation(
                        "Listing {Id}: owner '{Name}' verified after {Source}; stopping early.",
                        listing.Id, resolved.Value, source.SourceId);
                    break;
                }
            }
        }

        // Stage 2: reachable email.
        EmailStageResult? email = null;
        if (_emailStage is not null && !ct.IsCancellationRequested)
        {
            try { email = await _emailStage.ResolveAsync(listing, resolved, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Email stage failed for listing {Id}.", listing.Id); }
        }

        var rollup = new ListingEnrichment
        {
            ListingId = listing.Id,
            OwnerName = resolved.HasValue ? resolved.Value : null,
            OwnerNameVerified = resolved.Verified,
            OwnerNameConfidence = resolved.Confidence,
            OwnerNameSources = resolved.Sources.Count > 0 ? string.Join(",", resolved.Sources) : null,
            Email = email?.Email,
            EmailStatus = email?.Status ?? EmailVerificationStatus.NotFound,
            EmailConfidence = email?.Confidence ?? 0.0,
            Status = ListingEnrichmentStatus.Completed,
            FailureReason = resolved.HasValue ? null : OwnerFailureReason(listing),
            UpdatedAt = DateTime.UtcNow,
        };
        await _listings.UpsertEnrichmentAsync(rollup, ct);

        _logger.LogInformation(
            "Listing {Id} ({Name}): owner={Owner} verified={Verified} conf={Conf:0.00} sources=[{Sources}]",
            listing.Id, listing.Name, rollup.OwnerName ?? "-", rollup.OwnerNameVerified,
            rollup.OwnerNameConfidence, rollup.OwnerNameSources ?? "");

        return rollup;
    }

    private static string OwnerFailureReason(Listing listing) =>
        string.IsNullOrWhiteSpace(listing.Website) ? "no_website_no_registry_match" : "no_name_found";
}
