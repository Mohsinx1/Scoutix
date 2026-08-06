using Microsoft.Playwright;

namespace Scoutix.Enrichment.Scraping;

/// <summary>
/// One Chromium instance, lazily launched and reused across listings. Mirrors the launch flags and
/// resource-blocking the web app already uses for scraping (no-sandbox, /tmp shm, block
/// media/font/image) so behaviour matches production and RAM stays bounded.
/// </summary>
public sealed class PlaywrightBrowserProvider : IBrowserProvider
{
    private readonly string _userAgent;
    private readonly bool _headless;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    public PlaywrightBrowserProvider(string userAgent, bool headless = true)
    {
        _userAgent = userAgent;
        _headless = headless;
    }

    public async Task<TResult> RunAsync<TResult>(
        Func<IPage, CancellationToken, Task<TResult>> work, CancellationToken ct = default)
    {
        var browser = await EnsureBrowserAsync();

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            UserAgent = _userAgent,
        });

        var page = await context.NewPageAsync();
        await page.RouteAsync("**/*", async route =>
        {
            var type = route.Request.ResourceType;
            if (type is "media" or "font" or "image")
                await route.AbortAsync();
            else
                await route.ContinueAsync();
        });

        return await work(page, ct);
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is not null) return _browser;

        await _initLock.WaitAsync();
        try
        {
            if (_browser is not null) return _browser;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _headless,
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-extensions",
                    "--disable-notifications",
                    "--disable-background-networking",
                    "--disable-default-apps",
                    "--disable-sync",
                    "--mute-audio",
                    "--no-first-run",
                    "--disable-hang-monitor",
                },
            });
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
