namespace Scoutix.Enrichment.Email;

/// <summary>
/// Generates the standard owner-name → email guesses for a real business domain
/// (first, first.last, flast, firstl, …). Pure — the verifier decides which actually exist.
/// </summary>
public static class EmailPermutations
{
    public static IReadOnlyList<string> Generate(string? firstName, string? lastName, string? domain)
    {
        var first = Clean(firstName);
        var last = Clean(lastName);
        if (string.IsNullOrEmpty(domain) || EmailClassifier.IsFreeProviderDomain(domain))
            return Array.Empty<string>();
        if (string.IsNullOrEmpty(first) && string.IsNullOrEmpty(last))
            return Array.Empty<string>();

        var locals = new List<string>();
        void Add(string s) { if (!string.IsNullOrEmpty(s)) locals.Add(s); }

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            Add($"{first}.{last}");
            Add($"{first}{last}");
            Add($"{first[0]}{last}");      // flast
            Add($"{first}{last[0]}");      // firstl
            Add($"{first}_{last}");
            Add($"{first[0]}.{last}");     // f.last
            Add($"{last}.{first}");
            Add($"{last}{first[0]}");      // lastf
        }
        Add(first);
        Add(last);

        return locals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(l => $"{l}@{domain}".ToLowerInvariant())
            .ToList();
    }

    private static string Clean(string? name) =>
        new string((name ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
}
