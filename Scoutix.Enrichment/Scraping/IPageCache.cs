using Microsoft.EntityFrameworkCore;
using Scoutix.Enrichment.Data;

namespace Scoutix.Enrichment.Scraping;

/// <summary>
/// Caches fetched pages by URL so re-runs don't re-hit sources — being a polite scraper, and making
/// the pipeline cheap to resume. A cached empty/404 page is still a hit, so we don't retry dead URLs.
/// </summary>
public interface IPageCache
{
    Task<CachedPage?> GetAsync(string url, CancellationToken ct = default);
    Task PutAsync(string url, string? content, int? statusCode, CancellationToken ct = default);
}

/// <summary>No-op cache for probes/tests where persistence isn't wanted.</summary>
public sealed class NullPageCache : IPageCache
{
    public Task<CachedPage?> GetAsync(string url, CancellationToken ct = default) => Task.FromResult<CachedPage?>(null);
    public Task PutAsync(string url, string? content, int? statusCode, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>EF-backed cache against the <see cref="CachedPage"/> table (upsert on URL).</summary>
public sealed class EfPageCache : IPageCache
{
    private readonly EnrichmentDbContext _db;

    public EfPageCache(EnrichmentDbContext db) => _db = db;

    public async Task<CachedPage?> GetAsync(string url, CancellationToken ct = default) =>
        await _db.CachedPages.FirstOrDefaultAsync(p => p.Url == url, ct);

    public async Task PutAsync(string url, string? content, int? statusCode, CancellationToken ct = default)
    {
        var existing = await _db.CachedPages.FirstOrDefaultAsync(p => p.Url == url, ct);
        if (existing is null)
        {
            _db.CachedPages.Add(new CachedPage
            {
                Url = url,
                Content = content,
                StatusCode = statusCode,
                FetchedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Content = content;
            existing.StatusCode = statusCode;
            existing.FetchedAt = DateTime.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another concurrent listing cached the same URL first — that's fine, drop ours.
            _db.ChangeTracker.Clear();
        }
    }
}
