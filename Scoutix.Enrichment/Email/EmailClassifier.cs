using System.Text.RegularExpressions;

namespace Scoutix.Enrichment.Email;

/// <summary>
/// Pure, no-I/O classification of an email address: syntax, role vs personal, disposable domain,
/// free-mail provider. Used by the verifier and to decide whether name→email permutations even make
/// sense (they don't on gmail.com).
/// </summary>
public static class EmailClassifier
{
    private static readonly Regex SyntaxPattern = new(
        @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled);

    // Generic mailbox prefixes — deliverable, but not a specific person.
    private static readonly HashSet<string> RoleLocalParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "contact", "hello", "hi", "sales", "support", "admin", "office", "enquiry", "inquiry",
        "help", "billing", "accounts", "team", "mail", "email", "reception", "frontdesk", "front.desk",
        "appointments", "scheduling", "schedule", "smile", "smiles", "care", "service", "services",
        "no-reply", "noreply", "donotreply", "newpatients", "new.patients", "hr", "jobs", "marketing",
    };

    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "guerrillamail.com", "10minutemail.com", "tempmail.com", "temp-mail.org",
        "throwawaymail.com", "trashmail.com", "yopmail.com", "getnada.com", "sharklasers.com",
        "maildrop.cc", "dispostable.com", "fakeinbox.com",
    };

    private static readonly HashSet<string> FreeProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "yahoo.com", "ymail.com", "hotmail.com", "outlook.com",
        "live.com", "msn.com", "aol.com", "icloud.com", "me.com", "mac.com", "protonmail.com",
        "proton.me", "gmx.com", "comcast.net", "verizon.net", "att.net", "sbcglobal.net",
    };

    public static bool IsValidSyntax(string? email) =>
        !string.IsNullOrWhiteSpace(email) && SyntaxPattern.IsMatch(email);

    public static string? GetDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1 ? email[(at + 1)..].ToLowerInvariant() : null;
    }

    public static string? GetLocalPart(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : null;
    }

    public static bool IsRoleAddress(string? email) =>
        GetLocalPart(email) is { } local && RoleLocalParts.Contains(local);

    public static bool IsDisposableDomain(string? email) =>
        GetDomain(email) is { } domain && DisposableDomains.Contains(domain);

    /// <summary>True for gmail/outlook/etc. — capturing such an address is fine, but generating
    /// permutations against it is meaningless.</summary>
    public static bool IsFreeProvider(string? email) =>
        GetDomain(email) is { } domain && FreeProviders.Contains(domain);

    public static bool IsFreeProviderDomain(string? domain) =>
        domain is not null && FreeProviders.Contains(domain);
}
