namespace Scoutix.Enrichment.Model;

/// <summary>
/// Stable string identifiers for every source that can produce a candidate value.
/// Strings (not an enum) so new per-vertical/per-state resolvers can be added without a
/// schema migration — the source id is part of the candidate's natural key.
/// </summary>
public static class EnrichmentSources
{
    // ---- Owner-name sources ----
    public const string Website = "website";                 // crawled business site
    public const string SearchEngine = "search";             // SERP lookup
    public const string Npi = "npi";                         // NPPES NPI Registry API
    public const string StateLicenseBoard = "state_license_board"; // e.g. CO dental board (specifics in Detail)
    public const string SecretaryOfState = "sos";            // SoS / OpenCorporates officers

    // ---- Email sources ----
    public const string BusinessEmail = "business_email";    // email already on the listing/site
    public const string EmailPermutation = "email_permutation"; // generated first.last@domain etc.

    /// <summary>
    /// Splits owner-name sources into the report's two buckets:
    /// the commoditized web/search baseline vs. the public-records lift we're trying to prove.
    /// </summary>
    public static OwnerSourceBucket OwnerBucketOf(string source) => source switch
    {
        Website or SearchEngine => OwnerSourceBucket.WebOrSearch,
        Npi or StateLicenseBoard or SecretaryOfState => OwnerSourceBucket.Registry,
        _ => OwnerSourceBucket.Other,
    };
}

/// <summary>
/// The headline of the whole milestone hinges on this split: owners found by Registry sources
/// that WebOrSearch never produced are the lift over what Outscraper-style tools already do.
/// </summary>
public enum OwnerSourceBucket
{
    WebOrSearch,
    Registry,
    Other,
}
