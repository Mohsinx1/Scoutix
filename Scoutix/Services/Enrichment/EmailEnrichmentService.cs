using Microsoft.Playwright;
using Scoutix.Models;
using Scoutix.Models.Enums;
using Scoutix.Enrichment.Email;
using Sentry;

namespace Scoutix.Services.Enrichment;

public class EmailEnrichmentService : IEmailEnrichmentService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailExtractor _emailExtractor;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailEnrichmentService> _logger;

    // Paths to try when the homepage yields no emails
    private static readonly string[] ContactPaths =
    {
        "/contact", "/contact-us", "/about", "/about-us", "/team"
    };

    public EmailEnrichmentService(
        ApplicationDbContext context,
        IEmailExtractor emailExtractor,
        IWebHostEnvironment env,
        ILogger<EmailEnrichmentService> logger)
    {
        _context = context;
        _emailExtractor = emailExtractor;
        _env = env;
        _logger = logger;
    }

    public async Task<EnrichmentResult> EnrichLeadAsync(int leadId, CancellationToken ct = default)
    {
        Lead? lead;

        try
        {
            lead = await _context.Leads.FindAsync(new object[] { leadId }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnrichLeadAsync: DB error loading Lead {LeadId}.", leadId);
            SentrySdk.CaptureException(ex);
            return Fail("DB error loading lead.");
        }

        if (lead == null)
        {
            _logger.LogWarning("EnrichLeadAsync: Lead {LeadId} not found.", leadId);
            return Fail("Lead not found.");
        }

        if (string.IsNullOrWhiteSpace(lead.Website))
        {
            _logger.LogInformation("EnrichLeadAsync: Lead {LeadId} has no website.", leadId);
            return NoEmailFound();
        }

        _logger.LogInformation(
            "EnrichLeadAsync: Starting enrichment for Lead {LeadId}, website: {Website}",
            leadId, lead.Website);

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = !_env.IsDevelopment(), // headed in dev so you can watch; headless in production
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",      // Use /tmp instead of /dev/shm — required in Docker
                    "--disable-extensions",
                    "--disable-notifications",
                    "--disable-background-networking",
                    "--disable-default-apps",
                    "--disable-sync",
                    "--mute-audio",
                    "--no-first-run",
                    "--disable-hang-monitor",
                }
            });

            await using var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            });

            var page = await browserContext.NewPageAsync();

            // Block media, fonts, images — reduces RAM and speeds up page loads
            await page.RouteAsync("**/*", async route =>
            {
                var type = route.Request.ResourceType;
                if (type == "media" || type == "font" || type == "image")
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            // Step 1: try homepage
            var emails = await VisitAndExtractAsync(page, lead.Website, ct);

            // Step 2: try common contact/about paths if homepage had nothing
            if (emails.Count == 0)
            {
                var baseUrl = GetBaseUrl(lead.Website);
                foreach (var path in ContactPaths)
                {
                    if (ct.IsCancellationRequested) break;
                    emails = await VisitAndExtractAsync(page, baseUrl + path, ct);
                    if (emails.Count > 0) break;
                }
            }

            if (emails.Count == 0)
            {
                _logger.LogInformation("EnrichLeadAsync: No emails found for Lead {LeadId}.", leadId);
                return NoEmailFound();
            }

            var best = _emailExtractor.PickBest(emails)!;
            _logger.LogInformation(
                "EnrichLeadAsync: Best email for Lead {LeadId}: {Email} (from {Total} candidates)",
                leadId, best, emails.Count);

            // Return result — the Hangfire job owns all DB writes
            return new EnrichmentResult
            {
                Success = true,
                BestEmail = best,
                AllEmails = emails,
                FinalStatus = EnrichmentStatus.Completed,
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("EnrichLeadAsync: Cancelled for Lead {LeadId}.", leadId);
            return Fail("Job was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnrichLeadAsync: Unexpected error for Lead {LeadId}.", leadId);
            SentrySdk.CaptureException(ex);
            return Fail(ex.Message);
        }
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private async Task<List<string>> VisitAndExtractAsync(IPage page, string url, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("VisitAndExtractAsync: Visiting {Url}", url);

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 15000,
            });

            var html = await page.ContentAsync();
            var emails = _emailExtractor.Extract(html);

            _logger.LogInformation(
                "VisitAndExtractAsync: Found {Count} email(s) at {Url}", emails.Count, url);

            return emails;
        }
        catch (PlaywrightException ex)
        {
            // Covers: timeout, DNS failure, SSL error, 4xx/5xx, bot block
            _logger.LogWarning("VisitAndExtractAsync: Could not load {Url}. {Message}", url, ex.Message);
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VisitAndExtractAsync: Unexpected error for {Url}.", url);
            return new List<string>();
        }
    }

    private static string GetBaseUrl(string website)
    {
        try
        {
            var uri = new Uri(
                website.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? website
                    : "https://" + website);
            return $"{uri.Scheme}://{uri.Host}";
        }
        catch
        {
            return website.TrimEnd('/');
        }
    }

    private static EnrichmentResult NoEmailFound() => new()
    {
        Success = false,
        FinalStatus = EnrichmentStatus.NoEmailFound,
        FailureReason = "No email found on website.",
    };

    private static EnrichmentResult Fail(string reason) => new()
    {
        Success = false,
        FinalStatus = EnrichmentStatus.Failed,
        FailureReason = reason,
    };
}
