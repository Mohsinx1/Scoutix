namespace Scoutix.Enrichment.Data;

/// <summary>
/// A fetched page (HTML or API JSON) cached so re-runs don't re-hit the source — part of being a
/// polite scraper. Keyed by URL.
/// </summary>
public class CachedPage
{
    public int Id { get; set; }

    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? Content { get; set; }
    public DateTime FetchedAt { get; set; }
}
