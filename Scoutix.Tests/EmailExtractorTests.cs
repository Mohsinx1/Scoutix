using Scoutix.Enrichment.Email;

namespace Scoutix.Tests;

public class EmailExtractorTests
{
    private readonly EmailExtractor _extractor = new();

    // -----------------------------------------------------------------------
    // Extract() tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Extract_EmptyHtml_ReturnsEmptyList()
    {
        var result = _extractor.Extract("");
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_NullHtml_ReturnsEmptyList()
    {
        var result = _extractor.Extract(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_PlainEmail_ReturnsIt()
    {
        var html = "<p>Contact us at john@acmeplumbing.com for a quote.</p>";
        var result = _extractor.Extract(html);
        Assert.Single(result);
        Assert.Equal("john@acmeplumbing.com", result[0]);
    }

    [Fact]
    public void Extract_MailtoLink_ExtractsEmail()
    {
        var html = "<a href=\"mailto:info@bestroofing.com\">Email us</a>";
        var result = _extractor.Extract(html);
        Assert.Contains("info@bestroofing.com", result);
    }

    [Fact]
    public void Extract_JunkDomains_AreFiltered()
    {
        var html = @"
            analytics@google.com
            error@sentry.io
            test@example.com
            user@amazonaws.com
            real@localplumber.com
        ";
        var result = _extractor.Extract(html);
        Assert.Single(result);
        Assert.Equal("real@localplumber.com", result[0]);
    }

    [Fact]
    public void Extract_NoReplyLocalPart_IsFiltered()
    {
        var html = "noreply@legitimatebusiness.com and no-reply@shop.com";
        var result = _extractor.Extract(html);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_ImageFilenameFalsePositive_IsFiltered()
    {
        // Regex would naively match "photo@2x" from "photo@2x.png" in img src attributes
        var html = "<img src=\"assets/photo@2x.png\"> <img src=\"logo@3x.jpg\">";
        var result = _extractor.Extract(html);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_DuplicateEmails_DeduplicatedToOne()
    {
        var html = "info@smithcarpentry.com and info@smithcarpentry.com and INFO@smithcarpentry.com";
        var result = _extractor.Extract(html);
        Assert.Single(result);
    }

    [Fact]
    public void Extract_SubdomainOfJunkDomain_IsFiltered()
    {
        // e.g. Sentry uses subdomains like "o123.ingest.sentry.io"
        var html = "errors@o123.ingest.sentry.io should be filtered";
        var result = _extractor.Extract(html);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_MixedHtml_ReturnsOnlyGoodEmails()
    {
        var html = @"
            <html>
              <head><script>ga('send', 'event', 'contact@google.com')</script></head>
              <body>
                <p>Email us at <a href=""mailto:hello@greenlandscaping.com"">hello@greenlandscaping.com</a></p>
                <p>Or call. noreply@greenlandscaping.com for automated stuff.</p>
                <img src=""header@2x.png"">
              </body>
            </html>
        ";
        var result = _extractor.Extract(html);
        // Only hello@greenlandscaping.com should survive
        Assert.Single(result);
        Assert.Equal("hello@greenlandscaping.com", result[0]);
    }

    // -----------------------------------------------------------------------
    // PickBest() tests
    // -----------------------------------------------------------------------

    [Fact]
    public void PickBest_EmptyList_ReturnsNull()
    {
        var result = _extractor.PickBest(new List<string>());
        Assert.Null(result);
    }

    [Fact]
    public void PickBest_SingleCandidate_ReturnsThat()
    {
        var result = _extractor.PickBest(new List<string> { "john@acmeplumbing.com" });
        Assert.Equal("john@acmeplumbing.com", result);
    }

    [Fact]
    public void PickBest_InfoBeatsRandomName()
    {
        var candidates = new List<string>
        {
            "john@smithroofing.com",
            "info@smithroofing.com",
        };
        var result = _extractor.PickBest(candidates);
        Assert.Equal("info@smithroofing.com", result);
    }

    [Fact]
    public void PickBest_ContactBeatsRandomName()
    {
        var candidates = new List<string>
        {
            "sarah@bestelectric.com",
            "contact@bestelectric.com",
        };
        var result = _extractor.PickBest(candidates);
        Assert.Equal("contact@bestelectric.com", result);
    }

    [Fact]
    public void PickBest_InfoBeatsSales()
    {
        // info has lower index (higher priority) than sales in PriorityPrefixes
        var candidates = new List<string>
        {
            "sales@acmehvac.com",
            "info@acmehvac.com",
        };
        var result = _extractor.PickBest(candidates);
        Assert.Equal("info@acmehvac.com", result);
    }
}
