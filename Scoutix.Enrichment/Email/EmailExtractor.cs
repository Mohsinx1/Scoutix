using System.Text.RegularExpressions;

namespace Scoutix.Enrichment.Email;

public class EmailExtractor : IEmailExtractor
{
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly HashSet<string> JunkDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "example.com", "example.org", "example.net",
        "sentry.io",
        "google.com", "googleapis.com", "googletagmanager.com",
        "cloudflare.com", "cloudflareinsights.com",
        "w3.org", "schema.org",
        "wix.com", "wixpress.com",
        "wordpress.com", "wordpress.org",
        "squarespace.com",
        "weebly.com",
        "shopify.com",
        "mailchimp.com",
        "sendgrid.net",
        "gravatar.com",
        "amazonaws.com",
        "facebook.com",
        "twitter.com",
        "instagram.com",
        "linkedin.com",
        "yourdomain.com",
        "youremail.com",
        "domain.com",
    };

    private static readonly HashSet<string> JunkLocalParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "no-reply", "noreply", "donotreply", "do-not-reply",
        "unsubscribe", "bounce", "mailer-daemon", "postmaster",
    };

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".bmp" };

    // Priority order: earlier index = higher priority
    private static readonly string[] PriorityPrefixes =
    {
        "info", "contact", "hello", "hi", "sales", "support",
        "admin", "enquiry", "inquiry", "help", "office",
    };

    public List<string> Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new List<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (Match match in EmailPattern.Matches(html))
        {
            var email = match.Value.ToLowerInvariant();

            if (!seen.Add(email))
                continue;

            if (IsJunk(email))
                continue;

            result.Add(email);
        }

        return result;
    }

    public string? PickBest(List<string> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        return candidates
            .OrderBy(GetPriority)
            .ThenBy(e => e)
            .First();
    }

    private bool IsJunk(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex < 0) return true;

        var local = email[..atIndex];
        var domain = email[(atIndex + 1)..];

        // Catches things like "photo@2x.png" that regex grabs from img src attributes
        if (ImageExtensions.Any(ext => domain.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (JunkLocalParts.Contains(local))
            return true;

        if (JunkDomains.Contains(domain))
            return true;

        // Catch subdomains like "track.sentry.io"
        if (JunkDomains.Any(j => domain.EndsWith("." + j, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private int GetPriority(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex < 0) return PriorityPrefixes.Length;

        var local = email[..atIndex];
        var idx = Array.IndexOf(PriorityPrefixes, local.ToLowerInvariant());
        return idx >= 0 ? idx : PriorityPrefixes.Length;
    }
}
