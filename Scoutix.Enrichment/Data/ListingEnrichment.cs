using Scoutix.Enrichment.Model;

namespace Scoutix.Enrichment.Data;

/// <summary>
/// Per-listing rollup computed from the candidates: the final resolved owner and email, whether
/// each was verified, and a failure reason. Convenience for resumability and the coverage report —
/// the <see cref="FieldCandidate"/> rows remain the source of truth.
/// 1:1 with <see cref="Listing"/> (shares its key).
/// </summary>
public class ListingEnrichment
{
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    // ---- Owner name ----
    public string? OwnerName { get; set; }
    public bool OwnerNameVerified { get; set; }
    public double OwnerNameConfidence { get; set; }
    /// <summary>Comma-separated source ids that backed the resolved name (provenance for the report).</summary>
    public string? OwnerNameSources { get; set; }

    // ---- Email ----
    public string? Email { get; set; }
    public EmailVerificationStatus EmailStatus { get; set; } = EmailVerificationStatus.NotFound;
    public double EmailConfidence { get; set; }

    // ---- Run bookkeeping ----
    public ListingEnrichmentStatus Status { get; set; } = ListingEnrichmentStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime UpdatedAt { get; set; }
}
