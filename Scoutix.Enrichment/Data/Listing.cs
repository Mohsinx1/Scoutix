using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Data;

/// <summary>
/// A business sourced from Google Maps (or an imported CSV) that we attempt to enrich.
/// Isolated from the web app's production <c>Lead</c> table on purpose — this is an R&amp;D
/// dataset and must never write into customer data.
/// </summary>
public class Listing
{
    public int Id { get; set; }

    /// <summary>
    /// Stable natural key for the source business (e.g. a hash of name+address). Re-importing the
    /// same set upserts on this rather than creating duplicate listings.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }      // generic business email straight from the source, if any
    public string? Website { get; set; }
    public string? Address { get; set; }

    public string Vertical { get; set; } = string.Empty;  // e.g. "dentists"
    public string City { get; set; } = string.Empty;      // e.g. "Denver, CO"

    public DateTime CreatedAt { get; set; }

    public ICollection<FieldCandidate> Candidates { get; set; } = new List<FieldCandidate>();
    public ListingEnrichment? Enrichment { get; set; }
}
