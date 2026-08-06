using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Scoutix.Enrichment.Configuration;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;
using Scoutix.Enrichment.Scraping;

namespace Scoutix.Enrichment.Sources.Website;

/// <summary>
/// The commoditized baseline owner source: crawl the business website's about/team/contact pages
/// and pull names sitting next to ownership cues via <see cref="RoleNameExtractor"/>.
/// </summary>
public sealed class WebsiteOwnerSource : IOwnerNameSource
{
    // Highest-yield pages for an owner-operated practice. "" = homepage.
    private static readonly string[] CandidatePaths =
    {
        "", "/about", "/about-us", "/team", "/our-team",
        "/meet-the-dentist", "/meet-the-doctor", "/doctors", "/contact",
    };

    private const int MaxCandidatesReturned = 5;

    private readonly IBrowserProvider _browser;
    private readonly IPageCache _cache;
    private readonly RateLimitOptions _rateLimit;
    private readonly ILogger<WebsiteOwnerSource> _logger;

    public WebsiteOwnerSource(
        IBrowserProvider browser,
        IPageCache cache,
        RateLimitOptions rateLimit,
        ILogger<WebsiteOwnerSource> logger)
    {
        _browser = browser;
        _cache = cache;
        _rateLimit = rateLimit;
        _logger = logger;
    }

    public string SourceId => EnrichmentSources.Website;

    public bool CanHandle(Listing listing) => !string.IsNullOrWhiteSpace(listing.Website);

    public async Task<IReadOnlyList<OwnerNameCandidate>> FindOwnersAsync(OwnerLookupContext context, CancellationToken ct = default)
    {
        var listing = context.Listing;
        if (!CanHandle(listing)) return Array.Empty<OwnerNameCandidate>();

        var baseUrl = GetBaseUrl(listing.Website!);
        if (baseUrl is null)
        {
            _logger.LogWarning("WebsiteOwnerSource: unparseable website '{Website}' for listing {Id}.", listing.Website, listing.Id);
            return Array.Empty<OwnerNameCandidate>();
        }

        // canonical key -> best match across all pages crawled.
        var best = new Dictionary<string, RoleNameMatch>();

        await _browser.RunAsync(async (page, token) =>
        {
            foreach (var path in CandidatePaths)
            {
                if (token.IsCancellationRequested) break;

                var url = baseUrl + path;
                var text = await GetPageTextAsync(page, url, token);
                if (string.IsNullOrWhiteSpace(text)) continue;

                foreach (var m in RoleNameExtractor.Extract(text))
                {
                    var key = NameNormalizer.CanonicalKey(m.Name);
                    if (key is null) continue;
                    if (!best.TryGetValue(key, out var prev) || m.Confidence > prev.Confidence)
                        best[key] = m with { Role = $"{m.Role} @ {path switch { "" => "/", var p => p }}" };
                }

                // Strong ownership cue found — no need to keep hitting more pages.
                if (best.Values.Any(x => x.Confidence >= RoleNameExtractor.OwnerRoleConfidence))
                    break;
            }
            return 0;
        }, ct);

        var results = best.Values
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.Name)
            .Take(MaxCandidatesReturned)
            .Select(m => new OwnerNameCandidate(m.Name, m.Confidence, m.Role))
            .ToList();

        _logger.LogInformation(
            "WebsiteOwnerSource: listing {Id} ({Website}) -> {Count} owner candidate(s).",
            listing.Id, baseUrl, results.Count);

        return results;
    }

    private async Task<string?> GetPageTextAsync(IPage page, string url, CancellationToken ct)
    {
        var cached = await _cache.GetAsync(url, ct);
        if (cached is not null)
        {
            _logger.LogDebug("WebsiteOwnerSource: cache hit {Url}.", url);
            return cached.Content;
        }

        string? text = null;
        int? status = null;
        try
        {
            var resp = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 15000,
            });
            status = resp?.Status;

            if (status is null or >= 200 and < 400)
            {
                try { text = await page.InnerTextAsync("body", new PageInnerTextOptions { Timeout = 5000 }); }
                catch { text = await page.ContentAsync(); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per-page failure (Playwright timeout, DNS, SSL, bot block) must not abort the whole
            // crawl — treat as no content, cache empty so we don't retry, and move to the next page.
            _logger.LogDebug("WebsiteOwnerSource: could not load {Url}: {Message}", url, ex.Message);
        }

        await _cache.PutAsync(url, text ?? string.Empty, status, ct);
        await RandomDelayAsync(ct);
        return text;
    }

    private async Task RandomDelayAsync(CancellationToken ct)
    {
        var ms = Random.Shared.Next(_rateLimit.MinDelayMs, _rateLimit.MaxDelayMs + 1);
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    private static string? GetBaseUrl(string website)
    {
        try
        {
            var uri = new Uri(website.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? website
                : "https://" + website);
            return $"{uri.Scheme}://{uri.Host}";
        }
        catch
        {
            return null;
        }
    }
}
