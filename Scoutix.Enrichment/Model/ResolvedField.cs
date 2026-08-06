namespace Scoutix.Enrichment.Model;

/// <summary>
/// The outcome of collapsing all candidates for one field of one listing into a single answer:
/// the chosen value, whether independent sources agree on it, an aggregate confidence, and the
/// sources that backed it (for provenance and for the registry-lift report).
/// </summary>
public sealed class ResolvedField
{
    public string? Value { get; init; }
    public bool Verified { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public static ResolvedField None { get; } = new();
}
