namespace Scoutix.Enrichment.Model;

/// <summary>
/// The fields the pipeline can resolve for a listing. Deliberately channel-agnostic:
/// phone (and anything else) slots in later with no schema change — it's just another value here.
/// </summary>
public enum EnrichmentField
{
    OwnerName = 0,
    Email = 1,
}

/// <summary>
/// Outcome of trying to verify a single email address.
/// </summary>
public enum EmailVerificationStatus
{
    Unknown = 0,
    Verified = 1,      // MX + SMTP probe accepted the mailbox
    CatchAll = 2,      // domain accepts everything — needs an external catch-all verifier
    Invalid = 3,       // syntax bad, or SMTP rejected the mailbox
    Disposable = 4,    // throwaway/role domain
    NoDomain = 5,      // listing has no real domain to check
    NotFound = 6,      // no candidate address to verify
}

/// <summary>
/// Terminal state of a listing's enrichment run — used for resumability so a re-run
/// can skip listings that already finished.
/// </summary>
public enum ListingEnrichmentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
}
