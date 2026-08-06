using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Scraping;

namespace Scoutix.Enrichment.Sources.Npi;

/// <summary>
/// Thin client over the official NPPES NPI Registry REST API (no scraping). Responses are cached by
/// URL via <see cref="IPageCache"/> so re-runs and repeated queries don't re-hit CMS.
/// </summary>
internal sealed class NpiClient
{
    private const string BaseUrl = "https://npiregistry.cms.hhs.gov/api/";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IPageCache _cache;
    private readonly ILogger _logger;

    public NpiClient(HttpClient http, IPageCache cache, ILogger logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NpiResult>> SearchAsync(NpiQuery q, CancellationToken ct = default)
    {
        var url = BuildUrl(q);

        var cached = await _cache.GetAsync(url, ct);
        string? json = cached?.Content;

        if (json is null)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                json = await resp.Content.ReadAsStringAsync(ct);
                await _cache.PutAsync(url, json, (int)resp.StatusCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("NPI: request failed for {Url}: {Message}", url, ex.Message);
                return Array.Empty<NpiResult>();
            }
        }

        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<NpiResult>();

        try
        {
            var parsed = JsonSerializer.Deserialize<NpiResponse>(json, JsonOpts);
            return parsed?.Results ?? new List<NpiResult>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("NPI: could not parse response from {Url}: {Message}", url, ex.Message);
            return Array.Empty<NpiResult>();
        }
    }

    private static string BuildUrl(NpiQuery q)
    {
        var sb = new StringBuilder(BaseUrl).Append("?version=2.1");

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append('&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        Add("enumeration_type", q.EnumerationType);
        Add("first_name", q.FirstName);
        Add("last_name", q.LastName);
        Add("organization_name", q.OrganizationName);
        Add("taxonomy_description", q.TaxonomyDescription);
        Add("city", q.City);
        Add("state", q.State);
        sb.Append("&limit=").Append(Math.Clamp(q.Limit, 1, 200));

        return sb.ToString();
    }
}

internal sealed class NpiQuery
{
    public string EnumerationType { get; init; } = "NPI-1";  // NPI-1 = individual, NPI-2 = organization
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? OrganizationName { get; init; }
    public string? TaxonomyDescription { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public int Limit { get; init; } = 20;
}

// ---- API response shapes (only the fields we use) ----

internal sealed class NpiResponse
{
    [JsonPropertyName("result_count")] public int ResultCount { get; set; }
    [JsonPropertyName("results")] public List<NpiResult>? Results { get; set; }
}

internal sealed class NpiResult
{
    [JsonPropertyName("enumeration_type")] public string? EnumerationType { get; set; }
    // NPPES returns this as a JSON string, not a number — keep it a string.
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("basic")] public NpiBasic? Basic { get; set; }
    [JsonPropertyName("addresses")] public List<NpiAddress>? Addresses { get; set; }
    [JsonPropertyName("taxonomies")] public List<NpiTaxonomy>? Taxonomies { get; set; }
}

internal sealed class NpiBasic
{
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("credential")] public string? Credential { get; set; }
    [JsonPropertyName("organization_name")] public string? OrganizationName { get; set; }
    [JsonPropertyName("authorized_official_first_name")] public string? AuthorizedOfficialFirstName { get; set; }
    [JsonPropertyName("authorized_official_last_name")] public string? AuthorizedOfficialLastName { get; set; }
    [JsonPropertyName("authorized_official_title_or_position")] public string? AuthorizedOfficialTitle { get; set; }
}

internal sealed class NpiAddress
{
    [JsonPropertyName("address_purpose")] public string? AddressPurpose { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("telephone_number")] public string? TelephoneNumber { get; set; }
}

internal sealed class NpiTaxonomy
{
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("primary")] public bool Primary { get; set; }
}
