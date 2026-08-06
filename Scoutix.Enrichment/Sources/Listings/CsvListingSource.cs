using System.Security.Cryptography;
using System.Text;
using Scoutix.Enrichment.Data;

namespace Scoutix.Enrichment.Sources.Listings;

/// <summary>
/// Reads listings from a CSV with a header row. Recognised columns (case-insensitive):
/// name, phone, website, email, address, city. Missing city falls back to the configured batch city.
/// </summary>
public sealed class CsvListingSource : IListingSource
{
    private readonly string _path;
    private readonly string _vertical;
    private readonly string _defaultCity;
    private readonly int _limit;

    public CsvListingSource(string path, string vertical, string defaultCity, int limit = int.MaxValue)
    {
        _path = path;
        _vertical = vertical;
        _defaultCity = defaultCity;
        _limit = limit;
    }

    public async Task<IReadOnlyList<Listing>> ReadAsync(CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(_path, ct);
        if (lines.Length < 2) return Array.Empty<Listing>();

        var header = ParseLine(lines[0]).Select(h => h.Trim().Trim('﻿').ToLowerInvariant()).ToList();

        // Accept common column aliases so an Outscraper / Google-Maps export loads without editing.
        int Col(params string[] names)
        {
            foreach (var n in names)
            {
                var i = header.IndexOf(n);
                if (i >= 0) return i;
            }
            return -1;
        }

        int iName = Col("name", "title", "business_name"),
            iPhone = Col("phone", "phone_number", "phone_1", "telephone"),
            iWeb = Col("website", "site", "url", "domain", "web"),
            iEmail = Col("email", "email_1", "email_address"),
            iAddr = Col("address", "full_address", "formatted_address");

        var listings = new List<Listing>();
        for (int i = 1; i < lines.Length && listings.Count < _limit; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = ParseLine(lines[i]);

            string? Get(int idx) => idx >= 0 && idx < f.Count && !string.IsNullOrWhiteSpace(f[idx]) ? f[idx].Trim() : null;

            var name = Get(iName);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var phone = Get(iPhone);
            var address = Get(iAddr);

            listings.Add(new Listing
            {
                SourceKey = MakeSourceKey(name, phone, address),
                Name = name,
                Phone = phone,
                Website = Get(iWeb),
                Email = Get(iEmail),
                Address = address,
                City = _defaultCity,   // single-city batch: stamp the batch city so the report filter matches
                Vertical = _vertical,
                CreatedAt = DateTime.UtcNow,
            });
        }
        return listings;
    }

    /// <summary>Stable id for the business so re-imports upsert instead of duplicating.</summary>
    public static string MakeSourceKey(string name, string? phone, string? address)
    {
        var basis = $"{name}|{phone ?? address ?? string.Empty}".ToLowerInvariant().Trim();
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(basis)));
        return hash; // 32 hex chars, well under the 200-char column limit
    }

    // Minimal RFC-4180-ish parser: handles quoted fields, embedded commas, and "" escapes.
    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
