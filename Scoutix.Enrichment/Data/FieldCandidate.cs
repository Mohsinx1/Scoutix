using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Data;

/// <summary>
/// One (field, value, source, confidence, timestamp) observation for a listing.
/// The pipeline APPENDS candidates rather than overwriting, so every source's opinion is
/// preserved with provenance. Re-running a source UPSERTS its own row (unique on
/// ListingId + Field + Source) so re-runs never create duplicates.
/// </summary>
public class FieldCandidate
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public EnrichmentField Field { get; set; }

    /// <summary>The resolved value (e.g. "Jane Smith", "jane@acmedental.com").</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Stable source id — see <see cref="EnrichmentSources"/>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>0..1 confidence this source assigns to this value.</summary>
    public double Confidence { get; set; }

    /// <summary>Optional provenance detail: the URL, NPI number, license id, etc. that produced it.</summary>
    public string? Detail { get; set; }

    /// <summary>When this candidate was last produced (updated on every re-run of the source).</summary>
    public DateTime ObservedAt { get; set; }
}
