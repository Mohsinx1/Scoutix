namespace Scoutix.Enrichment.Sources.Npi;

/// <summary>
/// Pure helpers that decide how a listing maps to NPI records — surname extraction, phone
/// normalization, and city/state parsing. Precision here directly drives the report's accuracy, so
/// it's isolated and unit-tested.
/// </summary>
public static class NpiMatching
{
    private static readonly HashSet<string> BusinessWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "family", "dental", "dentistry", "dentist", "care", "clinic", "center", "centre", "smile",
        "smiles", "group", "associates", "association", "cosmetic", "general", "pediatric", "implant",
        "implants", "orthodontics", "orthodontic", "endodontics", "spa", "studio", "of", "the", "and",
        "pc", "llc", "pllc", "dds", "dmd", "co", "inc", "office", "modern", "gentle", "bright", "advanced",
    };

    /// <summary>Last 10 digits of a phone number, or null if there aren't 10.</summary>
    public static string? DigitsOnly(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : null;
    }

    /// <summary>"Denver, CO" → "CO". Null if no 2-letter state is present.</summary>
    public static string? ParseState(string? cityField)
    {
        if (string.IsNullOrWhiteSpace(cityField)) return null;
        var parts = cityField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = parts.LastOrDefault();
        return last is { Length: 2 } && last.All(char.IsLetter) ? last.ToUpperInvariant() : null;
    }

    /// <summary>"Denver, CO" → "Denver".</summary>
    public static string? ParseCity(string? cityField)
    {
        if (string.IsNullOrWhiteSpace(cityField)) return null;
        var parts = cityField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    /// <summary>Pull likely surnames out of a business name ("Miller Family Dental" → ["Miller"]).</summary>
    public static IReadOnlyList<string> DeriveSurnames(string? businessName)
    {
        if (string.IsNullOrWhiteSpace(businessName)) return Array.Empty<string>();
        return businessName
            .Split(new[] { ' ', ',', '.', '&', '-', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && t.All(char.IsLetter) && !BusinessWords.Contains(t))
            .Take(2)
            .ToList();
    }

    /// <summary>Trim legal suffixes (PC/LLC/PLLC) off the end so org-name search matches.</summary>
    public static string CleanOrgName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var tokens = name.Split(new[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(t => !t.Equals("PC", StringComparison.OrdinalIgnoreCase)
                         && !t.Equals("LLC", StringComparison.OrdinalIgnoreCase)
                         && !t.Equals("PLLC", StringComparison.OrdinalIgnoreCase));
        return string.Join(' ', tokens).Trim();
    }
}
