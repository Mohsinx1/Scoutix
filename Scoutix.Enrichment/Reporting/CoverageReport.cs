using System.Text;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;

namespace Scoutix.Enrichment.Reporting;

/// <summary>
/// Turns the persisted enrichment data into the milestone deliverable: a console coverage summary
/// and CSVs. The headline is the <b>registry-only lift</b> — owners the licensing-board/NPI layer
/// found that website+search scraping missed. Classification/aggregation are pure for unit testing.
/// </summary>
public static class CoverageReport
{
    public static IReadOnlyList<ClassifiedListing> Classify(IEnumerable<ListingReportRow> rows)
    {
        var result = new List<ClassifiedListing>();
        foreach (var row in rows)
        {
            var rollup = row.Rollup;
            var resolved = rollup?.OwnerName;
            var hasOwner = !string.IsNullOrWhiteSpace(resolved);
            var resolvedKey = NameNormalizer.CanonicalKey(resolved);

            bool baseline = false, registry = false;
            if (hasOwner && resolvedKey is not null)
            {
                foreach (var c in row.Candidates.Where(c => c.Field == EnrichmentField.OwnerName))
                {
                    if (NameNormalizer.CanonicalKey(c.Value) != resolvedKey) continue;
                    switch (EnrichmentSources.OwnerBucketOf(c.Source))
                    {
                        case OwnerSourceBucket.WebOrSearch: baseline = true; break;
                        case OwnerSourceBucket.Registry: registry = true; break;
                    }
                }
            }

            var registryOnly = hasOwner && registry && !baseline;
            var emailPresent = !string.IsNullOrWhiteSpace(rollup?.Email);

            result.Add(new ClassifiedListing
            {
                Row = row,
                HasOwner = hasOwner,
                Verified = rollup?.OwnerNameVerified == true,
                BaselineFound = baseline,
                RegistryFound = registry,
                RegistryOnly = registryOnly,
                EmailPresent = emailPresent,
                EmailVerified = rollup?.EmailStatus == EmailVerificationStatus.Verified,
                EmailCatchAll = rollup?.EmailStatus == EmailVerificationStatus.CatchAll,
                Classification = !hasOwner ? "none" : registryOnly ? "registry-only-lift" : "web/search",
            });
        }
        return result;
    }

    public static ReportMetrics Aggregate(IReadOnlyList<ClassifiedListing> rows, string vertical, string city)
    {
        var ownerConfs = rows.Where(r => r.HasOwner).Select(r => r.Row.Rollup!.OwnerNameConfidence).ToList();
        var emailConfs = rows.Where(r => r.EmailPresent).Select(r => r.Row.Rollup!.EmailConfidence).ToList();

        // Per-source contribution to a resolved owner.
        var contribution = new Dictionary<string, int>();
        foreach (var r in rows.Where(x => x.HasOwner))
        {
            var key = NameNormalizer.CanonicalKey(r.Row.Rollup!.OwnerName);
            var sources = r.Row.Candidates
                .Where(c => c.Field == EnrichmentField.OwnerName && NameNormalizer.CanonicalKey(c.Value) == key)
                .Select(c => c.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sources)
                contribution[s] = contribution.GetValueOrDefault(s) + 1;
        }

        var failures = new Dictionary<string, int>
        {
            ["no website (and no registry match)"] = rows.Count(r => !r.HasOwner && string.IsNullOrWhiteSpace(r.Row.Listing.Website)),
            ["had website but no name found"]      = rows.Count(r => !r.HasOwner && !string.IsNullOrWhiteSpace(r.Row.Listing.Website)),
            ["named but unverified (single source)"] = rows.Count(r => r.HasOwner && !r.Verified),
            ["email: no domain"]                   = rows.Count(r => r.Row.Rollup?.EmailStatus == EmailVerificationStatus.NoDomain),
            ["email: none found"]                  = rows.Count(r => r.Row.Rollup?.EmailStatus == EmailVerificationStatus.NotFound),
            ["email: catch-all unresolved"]        = rows.Count(r => r.EmailCatchAll),
        };

        return new ReportMetrics
        {
            Vertical = vertical,
            City = city,
            Total = rows.Count,
            NamedOwner = rows.Count(r => r.HasOwner),
            BaselineOwner = rows.Count(r => r.BaselineFound),
            RegistryOnlyLift = rows.Count(r => r.RegistryOnly),
            VerifiedOwner = rows.Count(r => r.Verified),
            AvgOwnerConfidence = ownerConfs.Count > 0 ? ownerConfs.Average() : 0,
            EmailCaptured = rows.Count(r => r.EmailPresent),
            SmtpVerified = rows.Count(r => r.EmailVerified),
            EmailCatchAll = rows.Count(r => r.EmailCatchAll),
            AvgEmailConfidence = emailConfs.Count > 0 ? emailConfs.Average() : 0,
            BothNamedAndCaptured = rows.Count(r => r.HasOwner && r.EmailPresent),
            BothVerifiedAndVerified = rows.Count(r => r.Verified && r.EmailVerified),
            OwnerSourceContribution = contribution,
            Failures = failures,
        };
    }

    public static string RenderConsole(ReportMetrics m)
    {
        var sb = new StringBuilder();
        string Line(string label, int n) => $"  {label,-34} {n,4}  ({m.Pct(n),5:0.0}%)";

        sb.AppendLine("================================================================");
        sb.AppendLine($"  Coverage Report — {m.Vertical} / {m.City}");
        sb.AppendLine("================================================================");
        sb.AppendLine($"  Listings processed:                {m.Total}");
        sb.AppendLine();
        sb.AppendLine("  OWNER NAME");
        sb.AppendLine(Line("Named owner (any source):", m.NamedOwner));
        sb.AppendLine(Line("  - website/search baseline:", m.BaselineOwner));
        sb.AppendLine(Line("  - REGISTRY-ONLY LIFT  *", m.RegistryOnlyLift));
        sb.AppendLine(Line("Verified (2+ independent sources):", m.VerifiedOwner));
        sb.AppendLine($"  {"Avg owner confidence:",-34} {m.AvgOwnerConfidence,5:0.00}");
        sb.AppendLine("  Owner candidates by source:");
        foreach (var kv in m.OwnerSourceContribution.OrderByDescending(k => k.Value))
            sb.AppendLine($"      {kv.Key,-26} {kv.Value,4}");
        sb.AppendLine();
        sb.AppendLine("  EMAIL");
        sb.AppendLine(Line("Captured + MX-valid:", m.EmailCaptured));
        sb.AppendLine(Line("SMTP-verified:", m.SmtpVerified));
        sb.AppendLine(Line("Catch-all (unresolved):", m.EmailCatchAll));
        sb.AppendLine($"  {"Avg email confidence:",-34} {m.AvgEmailConfidence,5:0.00}");
        sb.AppendLine();
        sb.AppendLine("  BOTH");
        sb.AppendLine(Line("Named owner + captured email:", m.BothNamedAndCaptured));
        sb.AppendLine(Line("Verified owner + verified email:", m.BothVerifiedAndVerified));
        sb.AppendLine();
        sb.AppendLine("  FAILURE BREAKDOWN");
        foreach (var kv in m.Failures)
            sb.AppendLine($"  {kv.Key,-44} {kv.Value,4}");
        sb.AppendLine("================================================================");
        return sb.ToString();
    }

    public static void WriteSummaryCsv(IReadOnlyList<ClassifiedListing> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("listing_id,name,website,phone,owner,owner_verified,owner_confidence,owner_sources,classification,email,email_status,email_confidence,failure_reason");
        foreach (var r in rows)
        {
            var l = r.Row.Listing;
            var e = r.Row.Rollup;
            sb.AppendLine(string.Join(',', new[]
            {
                C(l.Id.ToString()), C(l.Name), C(l.Website), C(l.Phone),
                C(e?.OwnerName), C(r.Verified.ToString()), C(e?.OwnerNameConfidence.ToString("0.00")),
                C(e?.OwnerNameSources), C(r.Classification),
                C(e?.Email), C(e?.EmailStatus.ToString()), C(e?.EmailConfidence.ToString("0.00")),
                C(e?.FailureReason),
            }));
        }
        File.WriteAllText(path, sb.ToString());
    }

    public static void WriteCandidatesCsv(IEnumerable<ListingReportRow> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("listing_id,listing_name,field,value,source,confidence,detail,observed_at");
        foreach (var row in rows)
            foreach (var c in row.Candidates.OrderBy(c => c.Field).ThenByDescending(c => c.Confidence))
                sb.AppendLine(string.Join(',', new[]
                {
                    C(row.Listing.Id.ToString()), C(row.Listing.Name), C(c.Field.ToString()),
                    C(c.Value), C(c.Source), C(c.Confidence.ToString("0.00")), C(c.Detail),
                    C(c.ObservedAt.ToString("u")),
                }));
        File.WriteAllText(path, sb.ToString());
    }

    // CSV-escape a field.
    private static string C(string? value)
    {
        value ??= "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
