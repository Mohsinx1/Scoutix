using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;
using Scoutix.Enrichment.Scraping;

namespace Scoutix.Enrichment.Email;

public interface IEmailStage
{
    Task<EmailStageResult> ResolveAsync(Listing listing, ResolvedField ownerName, CancellationToken ct = default);
}

public sealed record EmailStageResult(string? Email, EmailVerificationStatus Status, double Confidence);

/// <summary>
/// Stage 2 of the pipeline. Captures the business email already published for the listing (reusing
/// the pages the website crawl already cached, plus any email from the source), generates owner-name
/// permutations against a real domain, verifies everything via <see cref="IEmailVerifier"/>, persists
/// what survives, and returns the best reachable address.
/// </summary>
public sealed class EmailStage : IEmailStage
{
    private static readonly string[] ContactPaths = { "", "/contact", "/contact-us", "/about" };

    private readonly IEmailVerifier _verifier;
    private readonly IPageCache _cache;
    private readonly IEmailExtractor _extractor;
    private readonly ICandidateStore _candidates;
    private readonly ILogger<EmailStage> _logger;

    public EmailStage(IEmailVerifier verifier, IPageCache cache, IEmailExtractor extractor,
        ICandidateStore candidates, ILogger<EmailStage> logger)
    {
        _verifier = verifier;
        _cache = cache;
        _extractor = extractor;
        _candidates = candidates;
        _logger = logger;
    }

    public async Task<EmailStageResult> ResolveAsync(Listing listing, ResolvedField ownerName, CancellationToken ct = default)
    {
        var domain = RegistrableDomain(listing.Website);
        var found = new List<(string Email, EmailVerificationResult Result)>();

        // 1. Capture business emails (from the listing + cached website pages).
        var captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (EmailClassifier.IsValidSyntax(listing.Email))
            captured.Add(listing.Email!.Trim().ToLowerInvariant());

        if (domain is not null)
        {
            foreach (var prefix in new[] { $"https://{domain}", $"https://www.{domain}" })
                foreach (var path in ContactPaths)
                {
                    var cached = await _cache.GetAsync(prefix + path, ct);
                    if (!string.IsNullOrEmpty(cached?.Content))
                        foreach (var e in _extractor.Extract(cached.Content))
                            captured.Add(e);
                }
        }

        foreach (var email in captured)
        {
            var r = await _verifier.VerifyAsync(email, ct);
            if (IsUnusable(r.Status)) continue;
            found.Add((email, r));
            await _candidates.UpsertAsync(listing.Id, EnrichmentField.Email, email,
                EnrichmentSources.BusinessEmail, r.Confidence, r.Detail, ct);
        }

        // 2. Owner-name permutations on a real domain — only kept if they actually verify.
        if (ownerName.HasValue && domain is not null)
        {
            var (first, last) = SplitName(ownerName.Value!);
            foreach (var email in EmailPermutations.Generate(first, last, domain))
            {
                if (captured.Contains(email)) continue;
                var r = await _verifier.VerifyAsync(email, ct);
                if (r.Status != EmailVerificationStatus.Verified) continue;
                found.Add((email, r));
                await _candidates.UpsertAsync(listing.Id, EnrichmentField.Email, email,
                    EnrichmentSources.EmailPermutation, r.Confidence, r.Detail, ct);
            }
        }

        // 3. Best reachable address.
        var best = found
            .OrderByDescending(v => StatusRank(v.Result.Status))
            .ThenBy(v => v.Result.IsRole ? 1 : 0)
            .ThenByDescending(v => v.Result.Confidence)
            .Select(v => (v.Email, v.Result))
            .FirstOrDefault();

        var result = best.Email is null
            ? new EmailStageResult(null, domain is null ? EmailVerificationStatus.NoDomain : EmailVerificationStatus.NotFound, 0.0)
            : new EmailStageResult(best.Email, best.Result.Status, best.Result.Confidence);

        _logger.LogInformation("EmailStage: listing {Id} -> email={Email} status={Status}",
            listing.Id, result.Email ?? "-", result.Status);
        return result;
    }

    private static bool IsUnusable(EmailVerificationStatus s) =>
        s is EmailVerificationStatus.Invalid or EmailVerificationStatus.Disposable or EmailVerificationStatus.NoDomain;

    private static int StatusRank(EmailVerificationStatus s) => s switch
    {
        EmailVerificationStatus.Verified => 4,
        EmailVerificationStatus.CatchAll => 3,
        EmailVerificationStatus.Unknown => 2,
        _ => 0,
    };

    private static (string? First, string? Last) SplitName(string fullName)
    {
        var tokens = (NameNormalizer.Clean(fullName) ?? fullName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length switch
        {
            0 => (null, null),
            1 => (tokens[0], null),
            _ => (tokens[0], tokens[^1]),
        };
    }

    private static string? RegistrableDomain(string? website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;
        try
        {
            var uri = new Uri(website.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? website : "https://" + website);
            var host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }
        catch { return null; }
    }
}
