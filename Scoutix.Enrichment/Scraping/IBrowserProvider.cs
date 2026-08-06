using Microsoft.Playwright;

namespace Scoutix.Enrichment.Scraping;

/// <summary>
/// Hands out isolated Playwright pages over a single shared browser, so the batch doesn't pay the
/// cost of launching Chromium per listing. Each call gets its own browser context (cookie/cache
/// isolation) that is torn down when the work completes.
/// </summary>
public interface IBrowserProvider : IAsyncDisposable
{
    Task<TResult> RunAsync<TResult>(Func<IPage, CancellationToken, Task<TResult>> work, CancellationToken ct = default);
}
