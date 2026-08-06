using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Scoutix.Enrichment.Scraping;

namespace Scoutix.Enrichment.Sources.CoBoard;

/// <summary>
/// Client over Colorado's open-data professional-license dataset (Socrata SODA API, resource
/// 7s5z-vewr) — the clean, API-based alternative to scraping DORA's WebForms portal. Dentists are
/// licensetype "DEN". Responses cached by URL. An optional Socrata app token raises rate limits.
/// </summary>
internal sealed class CoLicenseClient
{
    private const string BaseUrl = "https://data.colorado.gov/resource/7s5z-vewr.json";
    private const string DentistLicenseType = "DEN";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IPageCache _cache;
    private readonly ILogger _logger;
    private readonly string? _appToken;

    public CoLicenseClient(HttpClient http, IPageCache cache, ILogger logger, string? appToken = null)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _appToken = appToken;
    }

    /// <summary>Searches active+inactive dentist (DEN) licensees by last name, optional first name and city.</summary>
    public async Task<IReadOnlyList<CoLicenseRecord>> SearchDentistsAsync(
        string? firstName, string lastName, string? city, CancellationToken ct = default)
    {
        var where = new StringBuilder($"licensetype='{DentistLicenseType}'");
        where.Append(" and upper(lastname)='").Append(Soql(lastName)).Append('\'');
        if (!string.IsNullOrWhiteSpace(firstName))
            where.Append(" and upper(firstname) like '").Append(Soql(firstName)).Append("%'");
        if (!string.IsNullOrWhiteSpace(city))
            where.Append(" and upper(city)='").Append(Soql(city)).Append('\'');

        var url = $"{BaseUrl}?$where={Uri.EscapeDataString(where.ToString())}&$limit=50";

        var cached = await _cache.GetAsync(url, ct);
        string? json = cached?.Content;

        if (json is null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(_appToken))
                    req.Headers.TryAddWithoutValidation("X-App-Token", _appToken);

                using var resp = await _http.SendAsync(req, ct);
                json = await resp.Content.ReadAsStringAsync(ct);
                await _cache.PutAsync(url, json, (int)resp.StatusCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("CO board: request failed for {Url}: {Message}", url, ex.Message);
                return Array.Empty<CoLicenseRecord>();
            }
        }

        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<CoLicenseRecord>();

        try
        {
            return JsonSerializer.Deserialize<List<CoLicenseRecord>>(json, JsonOpts) ?? new List<CoLicenseRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CO board: could not parse response from {Url}: {Message}", url, ex.Message);
            return Array.Empty<CoLicenseRecord>();
        }
    }

    // Uppercase (to pair with SoQL upper()) and escape single quotes for SoQL.
    private static string Soql(string value) => value.Trim().ToUpperInvariant().Replace("'", "''");
}

internal sealed class CoLicenseRecord
{
    [JsonPropertyName("firstname")] public string? FirstName { get; set; }
    [JsonPropertyName("lastname")] public string? LastName { get; set; }
    [JsonPropertyName("middlename")] public string? MiddleName { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("licensetype")] public string? LicenseType { get; set; }
    [JsonPropertyName("licensestatusdescription")] public string? Status { get; set; }

    public bool IsActive => string.Equals(Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);
}
