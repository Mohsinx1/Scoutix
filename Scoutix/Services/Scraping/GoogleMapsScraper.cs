using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PhoneNumbers;
using Scoutix.Models;
using Scoutix.Models.Enums;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace Scoutix.Services.Scraping
{
    public class GoogleMapsScraper : IGoogleMapsScraper
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<GoogleMapsScraper> _logger;
        private readonly ApplicationDbContext _context;
        int resultsCount = 0;

        public GoogleMapsScraper(IWebHostEnvironment env, ILogger<GoogleMapsScraper> logger, ApplicationDbContext context)
        {
            _env = env;
            _logger = logger;
            _context = context;
        }


        public async Task<Dictionary<string, bool>> ScrapeAndSaveLeadsAsync(
      int nicheId,
      string niche,
      IEnumerable<LeadGenQuery> queries,
      int requiredLeads,
      HashSet<string> existingPhones,
      int userId,
      CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var queryExhaustionMap = new Dictionary<string, bool>();

            try
            {
                _logger.LogInformation($"Starting {nameof(ScrapeAndSaveLeadsAsync)} for niche: {niche}, required leads: {requiredLeads}");

                var results = new List<Lead>();
                var processedPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("Launching Playwright browser instance.");
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = !_env.IsDevelopment(),
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",          // CRITICAL: Azure cannot set --shm-size, so Chrome must use /tmp instead
                        "--disable-extensions",
                        "--disable-notifications",
                        "--disable-background-networking",
                        "--disable-default-apps",
                        "--disable-sync",
                        "--mute-audio",
                        "--no-first-run",
                        "--disable-hang-monitor",
                        // NOTE: --no-zygote intentionally removed — broken on Ubuntu Linux Docker
                        // (Playwright issue #26849): causes renderer crashes, not prevents them
                    }
                });

                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                });

                var page = await context.NewPageAsync();
                await page.RouteAsync("**/*", async route =>
                {
                    var resourceType = route.Request.ResourceType;
                    // Block resources we don't need — reduces memory and prevents OOM crashes on Azure
                    if (resourceType == "media" || resourceType == "font" || resourceType == "image")
                    {
                        await route.AbortAsync();
                    }
                    else
                    {
                        await route.ContinueAsync();
                    }
                });

                bool navigated = false;
                for (int attempt = 1; attempt <= 3 && !navigated; attempt++)
                {
                    try
                    {
                        await page.GotoAsync("https://www.google.com/maps", new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.Commit,
                            Timeout = 60000
                        });
                        navigated = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Google Maps navigation attempt {attempt} failed: {ex.Message}");
                        if (attempt == 3) throw;
                        await page.WaitForTimeoutAsync(3000);
                    }
                }

                await TryHandleConsentAsync(page);

                // Wait for DOM + JS to fully render before looking for the search box
                // Google Maps is a heavy SPA — Commit fires too early on slow Azure containers
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 30000 });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"DOMContentLoaded wait timed out, continuing anyway: {ex.Message}");
                }

                // Wait for the search box to be ready before running any queries
                try
                {
                    await page.WaitForSelectorAsync("input[name='q'][role='combobox']", new PageWaitForSelectorOptions { Timeout = 60000 });
                    _logger.LogInformation("Search box is ready.");
                }
                catch (Exception ex)
                {
                    // Capture page state and send to Sentry so we can see what Google is showing on Azure
                    try
                    {
                        var pageTitle = await page.TitleAsync();
                        var pageUrl = page.Url;
                        var pageHtml = await page.ContentAsync();
                        var snippet = pageHtml.Length > 3000 ? pageHtml.Substring(0, 3000) : pageHtml;

                        SentrySdk.CaptureException(ex, scope =>
                        {
                            scope.SetExtra("page_title", pageTitle);
                            scope.SetExtra("page_url", pageUrl);
                            scope.SetExtra("page_html_snippet", snippet);
                            scope.SetExtra("timeout_message", ex.Message);
                        });

                        _logger.LogError($"Search box never appeared. Page title: '{pageTitle}', URL: '{pageUrl}'. Sent to Sentry with HTML snippet.");
                    }
                    catch (Exception debugEx)
                    {
                        _logger.LogError($"Search box never appeared and debug capture failed: {debugEx.Message}");
                    }
                    return queryExhaustionMap;
                }

                foreach (var query in queries)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (resultsCount >= requiredLeads) break;

                    int remaining = requiredLeads - resultsCount;
                    int perQueryTarget = Math.Min(remaining, 50);

                    _logger.LogInformation($"Running query: {query.Query} with target of {perQueryTarget} leads.");

                    bool isExhausted = await RunQueryOnSamePageAsync(
                        page, context, nicheId, niche, query.Query,
                        query.CountryId, query.StateId, query.CityId, query.CountryIso2,
                        results, processedCards, processedPhones,
                        existingPhones, perQueryTarget, userId,
                        cancellationToken);

                    queryExhaustionMap[query.Query] = isExhausted;

                    _logger.LogInformation($"Query: {query.Query} | Exhausted: {isExhausted}");
                }

                if (results.Any())
                {
                    _logger.LogInformation($"Saving {results.Count} leads to database.");
                    await SaveLeadsToDatabaseAsync(results, userId);
                }

                sw.Stop();
                _logger.LogInformation(
                    $"ScrapeSession Completed | LeadsSaved={results.Count} | " +
                    $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | " +
                    $"ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | " +
                    $"ElapsedMs={sw.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    $"ScrapeSession Failed | " +
                    $"ElapsedMinutes={sw.Elapsed.TotalMinutes:F2} | " +
                    $"ElapsedSeconds={sw.Elapsed.TotalSeconds:F2} | " +
                    $"ElapsedMs={sw.ElapsedMilliseconds}");
            }

            return queryExhaustionMap;
        }

        private async Task<bool> RunQueryOnSamePageAsync(
     IPage page,
     IBrowserContext browserContext,
     int nicheId,
     string niche,
     string query,
     int countryId,
     int? stateId,
     int? cityId,
     string countryIso2,
     List<Lead> results,
     HashSet<string> processedCards,
     HashSet<string> processedPhones,
     HashSet<string> existingPhones,
     int perQueryTarget,
     int userId,
     CancellationToken cancellationToken)
        {
            bool isExhausted = false;
            try
            {
                _logger.LogInformation($"Starting {nameof(RunQueryOnSamePageAsync)} for niche: {niche}, query: {query}, target count: {perQueryTarget}");

                int startCount = resultsCount;
                int targetCount = startCount + perQueryTarget;

                // Press Escape twice to dismiss any panel or overlay from previous query
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(300);
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(500);

                var searchBox = page.Locator("input[name='q'][role='combobox']");

                // Try normal click first, fall back to JS click if overlay is blocking
                try
                {
                    await searchBox.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Normal click failed ({ex.Message}), trying FocusAsync fallback.");
                    // EvaluateAsync hangs forever with --disable-gpu on Azure (JS execution context freezes).
                    // FocusAsync is selector-based and works correctly headless.
                    await searchBox.FocusAsync();
                    await page.WaitForTimeoutAsync(500);
                }

                await searchBox.SelectTextAsync();
                await searchBox.FillAsync(query);
                await page.Keyboard.PressAsync("Enter");

                // Wait for Google Maps to navigate to search results URL
                try
                {
                    await page.WaitForURLAsync(url => url.Contains("/search") || url.Contains("/maps/search"), new PageWaitForURLOptions { Timeout = 30000 });
                    _logger.LogInformation($"Navigated to search results URL: {page.Url}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"URL did not change to search results for query: {query}. Current URL: {page.Url}. {ex.Message}");
                }

                // Now wait for results to appear
                try
                {
                    await page.WaitForSelectorAsync("div[role='article']", new PageWaitForSelectorOptions { Timeout = 45000 });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"No results found for query: {query}. {ex.Message}");
                    return false;
                }

                await ProcessCardsAsync(page, results, processedCards, processedPhones,
                    existingPhones, targetCount, nicheId, niche,
                    countryId, stateId, cityId, countryIso2,
                    userId, cancellationToken);

                int stableScrolls = 0;
                while (resultsCount < targetCount && stableScrolls < 10)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (await HasReachedEndOfListAsync(page))
                    {
                        _logger.LogInformation("Google Maps: reached end of list.");
                        isExhausted = true;
                        break;
                    }

                    int before = processedPhones.Count;

                    try
                    {
                        // EvaluateAsync hangs forever with --disable-gpu on Azure.
                        // Hover the feed panel then send a wheel event — pure Playwright input APIs,
                        // no JS execution context required.
                        var feedPanel = page.Locator("div[role='feed']").First;
                        if (await feedPanel.CountAsync() > 0)
                        {
                            await feedPanel.HoverAsync(new LocatorHoverOptions { Timeout = 5000 });
                            await page.Mouse.WheelAsync(0, 1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Scroll failed: {ex.Message}");
                    }

                    await page.WaitForTimeoutAsync(1000);

                    await ProcessCardsAsync(page, results, processedCards, processedPhones,
                        existingPhones, targetCount, nicheId, niche,
                        countryId, stateId, cityId, countryIso2,
                        userId, cancellationToken);

                    stableScrolls = (processedPhones.Count == before) ? stableScrolls + 1 : 0;
                }

                if (stableScrolls >= 10)
                {
                    isExhausted = true;
                    _logger.LogInformation($"Query exhausted after 10 stable scrolls: {query}");
                }

                _logger.LogInformation($"Query completed. Total leads: {results.Count}, Exhausted: {isExhausted}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(RunQueryOnSamePageAsync)}");
            }

            return isExhausted;
        }

        private async Task ProcessCardsAsync(
    IPage page,
    List<Lead> results,
    HashSet<string> processedCards,
    HashSet<string> processedPhones,
    HashSet<string> existingPhones,
    int requiredLeads,
    int nicheId,
    string niche,
    int countryId,
    int? stateId,
    int? cityId,
    string countryIso2,
    int userId,
    CancellationToken ct)
        {
            try
            {
                _logger.LogInformation($"Starting {nameof(ProcessCardsAsync)} for {results.Count} results, niche: {niche}, required leads: {requiredLeads}");

                var cardsLocator = page.Locator("div[role='article']");
                var totalCards = await cardsLocator.CountAsync();
                _logger.LogInformation($"Found {totalCards} cards to process.");

                for (int i = 0; i < totalCards; i++)
                {
                    if (resultsCount >= requiredLeads || ct.IsCancellationRequested) break;

                    try
                    {
                        _logger.LogInformation($"Processing card {i + 1} of {totalCards}.");

                        using var cardCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cardCts.CancelAfter(TimeSpan.FromSeconds(45));

                        var card = cardsLocator.Nth(i);

                        // Strategy 1: read aria-label directly from the article element — the div[role='article']
                        // itself always carries aria-label="Business Name" per the actual Google Maps HTML.
                        // This requires zero DOM traversal and is the most stable selector possible.
                        string? name = (await card.GetAttributeAsync("aria-label", new LocatorGetAttributeOptions { Timeout = 5000 }))?.Trim();

                        // Strategy 2: the main anchor a.hfpxzc also carries aria-label="Business Name"
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            var anchor = card.Locator("a.hfpxzc").First;
                            if (await anchor.CountAsync() > 0)
                            {
                                name = (await anchor.GetAttributeAsync("aria-label", new LocatorGetAttributeOptions { Timeout = 5000 }))?.Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                    _logger.LogInformation("Name found via a.hfpxzc aria-label fallback.");
                            }
                        }

                        // Strategy 3: div.qBF1Pd.fontHeadlineSmall — confirmed present in actual HTML
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            var nameEl = card.Locator("div.qBF1Pd.fontHeadlineSmall");
                            if (await nameEl.CountAsync() > 0)
                            {
                                try
                                {
                                    name = (await nameEl.First.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5000 }))?.Trim();
                                    if (!string.IsNullOrWhiteSpace(name))
                                        _logger.LogInformation("Name found via div.qBF1Pd.fontHeadlineSmall fallback.");
                                }
                                catch { _logger.LogWarning("Timed out reading name via div.qBF1Pd.fontHeadlineSmall."); }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            _logger.LogWarning("Name element not found via any strategy, skipping this card.");
                            continue;
                        }

                        if (!processedCards.Add(name))
                        {
                            _logger.LogInformation($"Card {name} has already been processed, skipping.");
                            continue;
                        }

                        _logger.LogInformation($"Card name: {name}");

                        await card.ScrollIntoViewIfNeededAsync();
                        await page.WaitForTimeoutAsync(300);

                        var aTag = card.Locator("a.hfpxzc").First;
                        if (await aTag.CountAsync() == 0)
                            aTag = card.Locator("a").First;

                        if (await aTag.CountAsync() == 0)
                        {
                            _logger.LogWarning($"No <a> tag found for {name}. Skipping...");
                            continue;
                        }

                        _logger.LogInformation($"Clicking card for {name}.");
                        await aTag.ClickAsync(new LocatorClickOptions { Timeout = 10000 });

                        bool detailLoaded = await WaitForDetailsAsync(page, card, name, cardCts.Token);
                        if (!detailLoaded)
                        {
                            _logger.LogWarning($"Detail panel did not load for {name}. Skipping.");
                            continue;
                        }

                        var phone = "";
                        string? address = null;
                        string? website = null;

                        bool phoneVisible = false;
                        try
                        {
                            await page.WaitForSelectorAsync(
                                "button[data-item-id*='phone']",
                                new PageWaitForSelectorOptions
                                {
                                    Timeout = 20000,
                                    State = WaitForSelectorState.Attached
                                });

                            var pollSw = Stopwatch.StartNew();
                            while (pollSw.Elapsed.TotalSeconds < 10)
                            {
                                try
                                {
                                    var el = await page
                                        .QuerySelectorAsync("button[data-item-id*='phone']")
                                        .WaitAsync(TimeSpan.FromSeconds(3));
                                    if (el == null) break;
                                    var text = (await el.TextContentAsync()
                                        .WaitAsync(TimeSpan.FromSeconds(3)) ?? "").Trim();
                                    if (!string.IsNullOrWhiteSpace(text))
                                    {
                                        phoneVisible = true;
                                        break;
                                    }
                                }
                                catch { break; }
                                await Task.Delay(500);
                            }

                            if (!phoneVisible)
                                _logger.LogInformation($"Phone button found but text empty after polling for {name}.");
                        }
                        catch
                        {
                            _logger.LogWarning($"No phone button for {name}.");
                        }

                        if (!phoneVisible)
                            continue;

                        var rawPhone = await GetPhoneAsync(page);
                        if (rawPhone != null)
                            phone = NormalizePhone(rawPhone, countryIso2);

                        if (string.IsNullOrWhiteSpace(phone))
                        {
                            _logger.LogInformation($"No phone found for {name}, skipping.");
                            continue;
                        }

                        if (existingPhones.Contains(phone) || !processedPhones.Add(phone))
                        {
                            _logger.LogInformation($"Duplicate phone for {name}, skipping.");
                            continue;
                        }

                        _logger.LogInformation($"Phone: {phone}");

                        try
                        {
                            var addressEl = await page
                                .QuerySelectorAsync("button[data-item-id*='address']")
                                .WaitAsync(TimeSpan.FromSeconds(5));
                            if (addressEl != null)
                            {
                                address = (await addressEl.TextContentAsync()
                                    .WaitAsync(TimeSpan.FromSeconds(5)))?.Trim();
                                if (!string.IsNullOrEmpty(address))
                                {
                                    address = Regex.Replace(address, @"[\uE000-\uF8FF]", "");
                                    address = address.Replace("\n", " ").Replace("\r", " ").Replace("+", " ");
                                    address = Regex.Replace(address, @"\s+", " ").Trim();
                                    _logger.LogInformation($"Cleaned Address: {address}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to extract address for {name}: {ex.Message}");
                        }

                        try
                        {
                            var websiteEl = await page
                                .QuerySelectorAsync("a[data-item-id='authority']")
                                .WaitAsync(TimeSpan.FromSeconds(5));
                            if (websiteEl != null)
                            {
                                website = await websiteEl
                                    .GetAttributeAsync("href")
                                    .WaitAsync(TimeSpan.FromSeconds(5)) ?? "";
                                if (!string.IsNullOrWhiteSpace(website))
                                {
                                    website = CleanWebsiteUrl(website);
                                    _logger.LogInformation($"Website found: {website}");
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Website not found.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to extract website for {name}: {ex.Message}");
                        }

                        // phone must be non-empty to reach here (we continue'd above if empty)
                        results.Add(new Lead
                        {
                            Name = name,
                            Phone = phone,
                            Website = website,
                            Address = address,
                            NicheId = nicheId,
                            CountryId = countryId,
                            StateId = stateId,
                            CityId = cityId,
                            CreatedAt = DateTime.UtcNow
                        });

                        resultsCount++;

                        if (results.Count % 10 == 0)
                        {
                            _logger.LogInformation($"Saving leads in batches: {results.Count}");
                            bool saved = await SaveLeadsToDatabaseAsync(results, userId);
                            if (saved) results.Clear();
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning($"Card {i + 1} timed out after 45 seconds. Skipping.");
                        continue;
                    }
                    catch (Microsoft.Playwright.PlaywrightException ex) when (
                        ex.Message.Contains("not attached to the DOM", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning($"Playwright transient error (stale card). Skipping card index {i}. Message: {ex.Message}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error in method {nameof(ProcessCardsAsync)} while processing a card. Message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in method {nameof(ProcessCardsAsync)}. Message: {ex.Message}");
            }
        }

        private async Task<bool> WaitForDetailsAsync(IPage page, ILocator card, string expectedName, CancellationToken ct)
        {
            int retryCount = 3;
            int delayMs = 500;

            try
            {
                while (retryCount > 0 && !ct.IsCancellationRequested)
                {
                    _logger.LogInformation($"Waiting for detail panel for {expectedName}. Attempts remaining: {retryCount}");

                    bool isUpdated = false;
                    try
                    {
                        var handle = await page.WaitForFunctionAsync(
                            @"(expectedName) => {
                                const el = document.querySelector('h1.DUwDvf.lfPIob');
                                return el && el.innerText.trim().toLowerCase() === expectedName.toLowerCase();
                            }",
                            expectedName,
                            new PageWaitForFunctionOptions { Timeout = 10000 }
                        );
                        isUpdated = await handle.EvaluateAsync<bool>("v => v");
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning($"Timeout waiting for detail panel for {expectedName}. Retrying click...");
                    }
                    catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning($"Playwright timeout waiting for detail panel for {expectedName}. Retrying click...");
                    }

                    if (isUpdated)
                    {
                        _logger.LogInformation($"Detail panel loaded for {expectedName}.");
                        return true;
                    }

                    try
                    {
                        var aTag = card.Locator("a.hfpxzc").First;
                        if (await aTag.CountAsync() == 0)
                            aTag = card.Locator("a").First;

                        if (await aTag.CountAsync() == 0)
                        {
                            _logger.LogWarning($"Cannot retry click for {expectedName}: <a> not found.");
                            return false;
                        }

                        await aTag.ClickAsync(new LocatorClickOptions { Timeout = 8000 });
                        _logger.LogInformation($"Retry click for {expectedName}.");
                    }
                    catch (PlaywrightException ex) when (
                        ex.Message.Contains("not attached", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("detached", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning($"Card stale during retry click for {expectedName}: {ex.Message}");
                    }

                    retryCount--;
                    await page.WaitForTimeoutAsync(delayMs);
                    delayMs = Math.Min(delayMs * 2, 4000);
                }

                _logger.LogError($"Detail panel never loaded for {expectedName} after all retries.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(WaitForDetailsAsync)} for {expectedName}.");
                return false;
            }
        }

        private async Task<string?> GetPhoneAsync(IPage page)
        {
            // We already confirmed the button has non-empty text before calling this.
            // Use TextContentAsync (DOM-only, no JS execution) + WaitAsync (hard timeout)
            // to avoid hangs with --disable-gpu on Azure headless Chrome.
            try
            {
                var el = await page
                    .QuerySelectorAsync("button[data-item-id*='phone']")
                    .WaitAsync(TimeSpan.FromSeconds(5));
                if (el == null) return null;
                var text = (await el.TextContentAsync().WaitAsync(TimeSpan.FromSeconds(5)))?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    _logger.LogInformation($"Raw phone extracted: {text}");
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while extracting phone number.");
                return null;
            }
        }


        private string NormalizePhone(string? phone, string? countryIso2)
        {
            try
            {
                _logger.LogInformation($"Starting {nameof(NormalizePhone)} with phone: {phone} and countryIso2: {countryIso2 ?? "Unknown"}");
                if (string.IsNullOrWhiteSpace(phone))
                {
                    _logger.LogInformation("Phone is null or empty. Returning empty string.");
                    return "";
                }
                var cleaned = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
                _logger.LogInformation($"Cleaned phone number: {cleaned}");
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    _logger.LogInformation("Cleaned phone number is empty.");
                    return "";
                }
                PhoneNumberUtil phoneNumberUtil = PhoneNumberUtil.GetInstance();
                PhoneNumber parsedNumber;
                try
                {
                    parsedNumber = phoneNumberUtil.Parse(cleaned, countryIso2 ?? "US");
                    var formattedNumber = phoneNumberUtil.Format(parsedNumber, PhoneNumberFormat.INTERNATIONAL);
                    _logger.LogInformation($"Normalized phone number: {formattedNumber}");
                    return formattedNumber;
                }
                catch (NumberParseException ex)
                {
                    _logger.LogError(ex, "Error parsing phone number.");
                    return cleaned;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in method {nameof(NormalizePhone)}. StackTrace: {ex.StackTrace}");
                return phone ?? "";
            }
        }

        private async Task TryHandleConsentAsync(IPage page)
        {
            try
            {
                _logger.LogInformation($"Starting method {nameof(TryHandleConsentAsync)}");

                // Cover all Google consent button variants across regions and languages
                var consent = page.Locator(@"
                    button:has-text('Accept all'),
                    button:has-text('Accept All'),
                    button:has-text('Accept'),
                    button:has-text('I agree'),
                    button:has-text('Agree'),
                    button:has-text('Agree to all'),
                    button:has-text('Allow all'),
                    button[aria-label*='Accept'],
                    button[jsname='higCR'],
                    form[action*='consent'] button
                ").First;

                var consentCount = await consent.CountAsync();
                _logger.LogInformation($"Found {consentCount} consent button(s).");

                if (consentCount > 0)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to click the consent button.");
                        await consent.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
                        _logger.LogInformation("Consent button clicked successfully.");
                        await page.WaitForTimeoutAsync(2000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error clicking consent button: {ex.Message}");
                    }
                }
                else
                {
                    _logger.LogInformation("No consent button found.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(TryHandleConsentAsync)}. StackTrace: {ex.StackTrace}");
            }
        }

        private async Task<bool> SaveLeadsToDatabaseAsync(List<Lead> leads, int userId)
        {
            try
            {
                _logger.LogInformation($"Starting method {nameof(SaveLeadsToDatabaseAsync)}");
                _logger.LogInformation($"Attempting to add {leads.Count} leads to the database.");

                await _context.Leads.AddRangeAsync(leads);
                await _context.SaveChangesAsync();

                var userLeads = leads.Select(l => new UserLead
                {
                    UserId = userId,
                    LeadId = l.Id,
                    Status = (int)LeadStatus.New,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                await _context.UserLeads.AddRangeAsync(userLeads);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"{leads.Count} leads and {userLeads.Count} UserLeads rows saved to database.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in method {nameof(SaveLeadsToDatabaseAsync)}. StackTrace: {ex.StackTrace}");
                return false;
            }
        }


        private string CleanWebsiteUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return string.Empty;

                if (url.StartsWith("/url?q=") || url.Contains("/url?q="))
                {
                    var uri = new Uri("https://www.google.com" + url);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var actualUrl = query["q"];

                    if (!string.IsNullOrWhiteSpace(actualUrl))
                        return System.Web.HttpUtility.UrlDecode(actualUrl);
                }

                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in method {nameof(CleanWebsiteUrl)}. StackTrace: {ex.StackTrace}");
                return url;
            }
        }

        private async Task<bool> HasReachedEndOfListAsync(IPage page)
        {
            var endMsg = page.Locator("span.HlvSq:has-text(\"You've reached the end of the list.\")");
            return await endMsg.CountAsync() > 0 && await endMsg.First.IsVisibleAsync();
        }
    }
}
