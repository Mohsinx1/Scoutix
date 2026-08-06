using System.Text.RegularExpressions;

namespace Scoutix.Enrichment.Resolution;

/// <summary>
/// Pure helpers for comparing people's names across sources. A website might say
/// "Dr. Jane A. Smith, DDS" while NPI says "JANE SMITH" — these must be recognised as the same
/// person. We do that by reducing each name to a canonical "first last" key.
/// </summary>
public static class NameNormalizer
{
    // Honorifics stripped from the front.
    private static readonly HashSet<string> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "doctor", "mr", "mrs", "ms", "miss", "prof", "professor",
    };

    // Credential / generational suffixes stripped from the end.
    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dds", "dmd", "md", "do", "phd", "facd", "pc", "pa", "dr",
        "jr", "sr", "ii", "iii", "iv", "esq",
    };

    private static readonly Regex NonNameChars = new(@"[^\p{L}\s'\-,]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Cleaned, human-readable form (titles/credentials removed, tidy spacing, original casing-ish).
    /// Returns null if nothing name-like survives.
    /// </summary>
    public static string? Clean(string? raw)
    {
        var tokens = Tokenize(raw);
        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    /// <summary>
    /// Pretty-cases a name for display. All-caps ("KARI MILLER", from NPPES) or all-lower tokens get
    /// title-cased; already-mixed tokens ("McDonald") are left alone.
    /// </summary>
    public static string ToDisplayCase(string? cleaned)
    {
        if (string.IsNullOrWhiteSpace(cleaned)) return cleaned ?? string.Empty;
        var ti = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
        return string.Join(' ', cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(tok =>
            {
                var mixed = tok.Any(char.IsLower) && tok.Any(char.IsUpper);
                return mixed ? tok : ti.ToTitleCase(tok.ToLowerInvariant());
            }));
    }

    /// <summary>
    /// Lower-cased "first last" key used for agreement between sources. Middle names/initials are
    /// dropped so "Jane A. Smith" and "Jane Smith" collide. Returns null if not enough name.
    /// </summary>
    public static string? CanonicalKey(string? raw)
    {
        var tokens = Tokenize(raw);
        if (tokens.Count == 0) return null;
        if (tokens.Count == 1) return tokens[0].ToLowerInvariant();

        var first = tokens[0];
        var last = tokens[^1];
        return $"{first} {last}".ToLowerInvariant();
    }

    /// <summary>True if two raw names reduce to the same person.</summary>
    public static bool Match(string? a, string? b)
    {
        var ka = CanonicalKey(a);
        var kb = CanonicalKey(b);
        return ka is not null && ka == kb;
    }

    private static List<string> Tokenize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

        // Handle "Smith, Jane" → "Jane Smith"
        var s = raw;
        var comma = s.IndexOf(',');
        if (comma > 0)
        {
            var before = s[..comma];
            var after = s[(comma + 1)..];
            // Only flip if the part after the comma looks like a given name, not a credential.
            var afterFirst = after.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (afterFirst is not null && !Suffixes.Contains(afterFirst.Trim('.')))
                s = $"{after} {before}";
            else
                s = before; // drop trailing credentials after the comma
        }

        s = NonNameChars.Replace(s, " ");
        s = Whitespace.Replace(s, " ").Trim();

        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', '-', '\''))
            .Where(t => t.Length > 0)
            .ToList();

        // Strip leading titles.
        while (tokens.Count > 0 && Titles.Contains(tokens[0]))
            tokens.RemoveAt(0);

        // Strip trailing credential/generational suffixes.
        while (tokens.Count > 0 && Suffixes.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        // Drop single-letter middle initials (keep first/last even if single letter).
        if (tokens.Count > 2)
        {
            tokens = new[] { tokens[0] }
                .Concat(tokens.Skip(1).Take(tokens.Count - 2).Where(t => t.Length > 1))
                .Append(tokens[^1])
                .ToList();
        }

        return tokens;
    }
}
