namespace Scoutix.Enrichment.Configuration;

/// <summary>
/// Everything the batch needs to run, bound from appsettings/CLI. The whole point is to re-target
/// "dentists / Denver / 100" → some other vertical/city/count with no code change.
/// </summary>
public class EnrichmentConfig
{
    public string Vertical { get; set; } = "dentists";
    public string City { get; set; } = "Denver, CO";
    public string State { get; set; } = "CO";       // 2-letter; picks the state licensing board
    public int TargetCount { get; set; } = 100;

    /// <summary>Connection string for the isolated enrichment database.</summary>
    public string ConnectionString { get; set; } =
        "Server=localhost;Database=ScoutixEnrichmentDb;Trusted_Connection=True;TrustServerCertificate=True;";

    public string UserAgent { get; set; } = "ScoutixEnrichmentBot/1.0 (+https://scoutix.io/bot)";

    /// <summary>Listings enriched in parallel. Each gets its own DbContext, so &gt;1 is safe.</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>Optional Socrata app token for the Colorado open-data API (raises rate limits).</summary>
    public string? ColoradoAppToken { get; set; }

    public SourceToggles Sources { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
    public EmailVerifyOptions Email { get; set; } = new();
}

/// <summary>SMTP verification knobs. SMTP needs outbound port 25 (often blocked) — when it can't run,
/// emails come back Unknown rather than falsely Invalid.</summary>
public class EmailVerifyOptions
{
    public bool SmtpProbe { get; set; } = true;
    public int SmtpTimeoutMs { get; set; } = 5000;
    public string MailFrom { get; set; } = "verify@scoutix.io";
    public string EhloDomain { get; set; } = "scoutix.io";
}

/// <summary>Per-source on/off switches. Search + SoS are off by default (conservative posture).</summary>
public class SourceToggles
{
    public bool Website { get; set; } = true;
    public bool Npi { get; set; } = true;
    public bool StateLicenseBoard { get; set; } = true;
    public bool SearchEngine { get; set; } = false;
    public bool SecretaryOfState { get; set; } = false;
    public bool EmailVerification { get; set; } = true;
}

/// <summary>Politeness knobs: randomized per-domain delays and transient-failure backoff.</summary>
public class RateLimitOptions
{
    public int MinDelayMs { get; set; } = 1500;
    public int MaxDelayMs { get; set; } = 4000;
    public int MaxRetries { get; set; } = 3;
    public int BackoffBaseMs { get; set; } = 1000;
}
