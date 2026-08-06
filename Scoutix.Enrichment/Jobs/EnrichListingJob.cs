using Hangfire;
using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Data;

namespace Scoutix.Enrichment.Jobs;

/// <summary>
/// Hangfire seam so the engine can run inside the main Scoutix app as a background service: one job
/// per listing on the <c>enrichment</c> queue, plus a recurring re-verification sweep (contact data
/// decays ~2%/month). The console runner enriches in-process and does NOT use this — the host app
/// registers <see cref="ListingEnricher"/> and its sources in DI, then enqueues/schedules these.
/// </summary>
public sealed class EnrichListingJob
{
    private readonly ListingEnricher _enricher;
    private readonly IListingStore _listings;
    private readonly ILogger<EnrichListingJob> _logger;

    public EnrichListingJob(ListingEnricher enricher, IListingStore listings, ILogger<EnrichListingJob> logger)
    {
        _enricher = enricher;
        _listings = listings;
        _logger = logger;
    }

    /// <summary>
    /// Enrich one listing. Enqueue with:
    /// <c>BackgroundJob.Enqueue&lt;EnrichListingJob&gt;(j =&gt; j.RunAsync(listingId, default));</c>
    /// </summary>
    [Queue("enrichment")]
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(int listingId, CancellationToken ct = default)
    {
        var listing = await _listings.GetListingAsync(listingId, ct);
        if (listing is null)
        {
            _logger.LogWarning("EnrichListingJob: listing {Id} not found.", listingId);
            return;
        }
        await _enricher.EnrichAsync(listing, forceReRun: false, ct);
    }

    /// <summary>
    /// Recurring re-verification of stale listings. Register in the host, e.g.:
    /// <c>RecurringJob.AddOrUpdate&lt;EnrichListingJob&gt;("enrichment-reverify",
    ///   j =&gt; j.ReVerifyStaleAsync(TimeSpan.FromDays(30), default), Cron.Weekly());</c>
    /// </summary>
    [Queue("enrichment")]
    public async Task ReVerifyStaleAsync(TimeSpan staleAfter, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - staleAfter;
        var stale = await _listings.GetListingsNeedingReVerificationAsync(cutoff, ct);
        _logger.LogInformation("EnrichListingJob: re-verifying {Count} stale listing(s).", stale.Count);

        foreach (var listing in stale)
        {
            if (ct.IsCancellationRequested) break;
            await _enricher.EnrichAsync(listing, forceReRun: true, ct);
        }
    }
}
