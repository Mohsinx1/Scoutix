using Scoutix.Enrichment.Data;

namespace Scoutix.Enrichment.Reporting;

/// <summary>One listing plus everything we recorded for it — the input to the report.</summary>
public sealed record ListingReportRow(
    Listing Listing,
    ListingEnrichment? Rollup,
    IReadOnlyList<FieldCandidate> Candidates);

/// <summary>A listing classified for the report's headline buckets.</summary>
public sealed class ClassifiedListing
{
    public required ListingReportRow Row { get; init; }
    public bool HasOwner { get; init; }
    public bool Verified { get; init; }
    public bool BaselineFound { get; init; }   // website/search produced the resolved owner
    public bool RegistryFound { get; init; }   // NPI/board/SoS produced the resolved owner
    public bool RegistryOnly { get; init; }    // registry found it AND website/search did NOT  ← the lift
    public bool EmailPresent { get; init; }
    public bool EmailVerified { get; init; }
    public bool EmailCatchAll { get; init; }
    public string Classification { get; init; } = "none";
}

/// <summary>Aggregate coverage numbers for the console summary.</summary>
public sealed class ReportMetrics
{
    public string Vertical { get; init; } = "";
    public string City { get; init; } = "";
    public int Total { get; init; }

    public int NamedOwner { get; init; }
    public int BaselineOwner { get; init; }       // website/search got the resolved owner
    public int RegistryOnlyLift { get; init; }    // ★ owners only the registry layer found
    public int VerifiedOwner { get; init; }
    public double AvgOwnerConfidence { get; init; }

    public int EmailCaptured { get; init; }       // captured + MX-valid (status not failed)
    public int SmtpVerified { get; init; }        // SMTP-confirmed deliverable
    public int EmailCatchAll { get; init; }
    public double AvgEmailConfidence { get; init; }

    public int BothNamedAndCaptured { get; init; }
    public int BothVerifiedAndVerified { get; init; }

    public Dictionary<string, int> OwnerSourceContribution { get; init; } = new();
    public Dictionary<string, int> Failures { get; init; } = new();

    public double Pct(int n) => Total == 0 ? 0 : 100.0 * n / Total;
}
