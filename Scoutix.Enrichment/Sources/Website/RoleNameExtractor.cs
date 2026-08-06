using System.Net;
using System.Text.RegularExpressions;
using Scoutix.Enrichment.Resolution;

namespace Scoutix.Enrichment.Sources.Website;

/// <summary>One person-name found on a page, the role cue that flagged it, and a confidence.</summary>
public sealed record RoleNameMatch(string Name, string Role, double Confidence);

/// <summary>
/// Pure heuristic: given page text/HTML, find person names sitting next to ownership cues
/// ("Jane Smith, Owner", "Founder: John Doe", "Dr. Emily Carter"). No I/O — fully unit-testable.
/// This is the commoditized baseline; the registry sources are the high-precision ones.
/// </summary>
public static class RoleNameExtractor
{
    // Confidence tiers by how strongly the cue implies *ownership*.
    public const double OwnerRoleConfidence = 0.65;   // owner / founder / principal / proprietor
    public const double ExecRoleConfidence = 0.55;    // president / ceo / practice owner / lead dentist
    public const double TitleOnlyConfidence = 0.45;   // "Dr. <Name>" with no explicit ownership cue

    // Role alternation (case-insensitive at use site). Order matters only for readability.
    private const string Roles =
        @"owner|co-?owner|founder|co-?founder|proprietor|principal|president|ceo|practice\s+owner|lead\s+dentist|managing\s+partner";

    // A person name: optional middle token/initial, 2-3 capitalized tokens.
    private const string NameCore =
        // Full-word branch FIRST so "Carter" wins over the single-initial branch; the initial
        // branch is the fallback for genuine middle initials ("A."). Inter-token whitespace is
        // [^\S\n] (no newline) so a name can't span two separate headings/lines.
        @"[A-Z][a-zA-Z'’\-]+(?:[^\S\n]+(?:[A-Z][a-zA-Z'’\-]+|[A-Z]\.?)){1,2}";

    // P1: "<Name>[, DDS][,/-/newline] <role>". The credential is its own optional group so a single
    // comma in "Name, Owner" is consumed once, by the separator — not double-counted.
    private static readonly Regex NameThenRole = new(
        $@"(?<name>(?:[Dd]r\.?\s+)?{NameCore})(?:[,\s]+(?:DDS|DMD|MD|DO))?[\s,:|\-–—]{{1,3}}(?:the\s+|our\s+)?(?<role>(?i:{Roles}))",
        RegexOptions.Compiled);

    // P2: "<role>[:/-] [Dr.] <Name>"
    private static readonly Regex RoleThenName = new(
        $@"(?<role>(?i:{Roles}))\b[\s,:|\-–—]{{0,3}}(?:is\s+|by\s+|are\s+)?(?:[Dd]r\.?\s+)?(?<name>{NameCore})",
        RegexOptions.Compiled);

    // P3: "Dr. <Name>" — title-anchored, lower confidence (could be an associate, not the owner).
    private static readonly Regex TitleName = new(
        $@"\b[Dd]r\.?\s+(?<name>{NameCore})",
        RegexOptions.Compiled);

    private static readonly Regex ScriptStyle = new(
        @"<(script|style)[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"[^\S\n]+", RegexOptions.Compiled);

    // Words that look like names to the regex but aren't people.
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dental", "Dentistry", "Dentist", "Care", "Clinic", "Center", "Centre", "Smile", "Smiles",
        "Family", "Group", "Associates", "Association", "Office", "Practice", "Team", "Staff",
        "Welcome", "Contact", "About", "Services", "Service", "New", "Patients", "Patient", "Our",
        "Your", "Insurance", "Emergency", "Cosmetic", "General", "Pediatric", "Implants", "Implant",
        "Orthodontics", "Orthodontic", "Hours", "Location", "Locations", "Reviews", "Review",
        "Appointment", "Appointments", "Today", "Home", "Page", "Meet", "Read", "More", "Health",
        "Modern", "Gentle", "Bright", "North", "South", "East", "West", "Downtown", "City",
        // Role / credential words the regex can otherwise swallow into a name.
        "Owner", "Owners", "Founder", "Principal", "President", "Proprietor", "Partner",
        "Doctor", "Associate", "Hygienist", "Voted",
    };

    public static IReadOnlyList<RoleNameMatch> Extract(string? htmlOrText)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText))
            return Array.Empty<RoleNameMatch>();

        var text = ToText(htmlOrText);

        // canonical key -> best match, so the same person isn't reported twice.
        var best = new Dictionary<string, RoleNameMatch>();

        void Consider(string rawName, string role, double confidence)
        {
            var name = NameNormalizer.Clean(rawName);
            if (name is null || !IsPlausibleName(name)) return;

            var key = NameNormalizer.CanonicalKey(name);
            if (key is null) return;

            if (!best.TryGetValue(key, out var prev) || confidence > prev.Confidence)
                best[key] = new RoleNameMatch(name, role, confidence);
        }

        foreach (Match m in NameThenRole.Matches(text))
            Consider(m.Groups["name"].Value, NormalizeRole(m.Groups["role"].Value), RoleConfidence(m.Groups["role"].Value));

        foreach (Match m in RoleThenName.Matches(text))
            Consider(m.Groups["name"].Value, NormalizeRole(m.Groups["role"].Value), RoleConfidence(m.Groups["role"].Value));

        foreach (Match m in TitleName.Matches(text))
            Consider(m.Groups["name"].Value, "doctor", TitleOnlyConfidence);

        return best.Values
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static string ToText(string html)
    {
        var s = ScriptStyle.Replace(html, " ");
        s = Tags.Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Whitespace.Replace(s, " ");
        return s;
    }

    private static bool IsPlausibleName(string cleaned)
    {
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 2 or > 3) return false;
        if (tokens.Any(t => Stopwords.Contains(t))) return false;
        // First and last token must be real words (≥2 letters), not initials/acronyms.
        if (tokens[0].Length < 2 || tokens[^1].Length < 2) return false;
        if (tokens.Any(t => t.Any(char.IsDigit))) return false;
        return true;
    }

    private static string NormalizeRole(string role) =>
        Whitespace.Replace(role.Trim().ToLowerInvariant(), " ").Replace("-", "");

    private static double RoleConfidence(string role)
    {
        var r = NormalizeRole(role);
        return r switch
        {
            "owner" or "coowner" or "founder" or "cofounder" or "proprietor" or "principal" => OwnerRoleConfidence,
            _ => ExecRoleConfidence,
        };
    }
}
