using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Reporting;

namespace Scoutix.Tests;

public class CoverageReportTests
{
    private static FieldCandidate Owner(string value, string source) =>
        new() { Field = EnrichmentField.OwnerName, Value = value, Source = source, Confidence = 0.8 };

    private static ListingEnrichment Rollup(string name, bool verified, string sources,
        string? email = null, EmailVerificationStatus es = EmailVerificationStatus.NotFound) =>
        new()
        {
            OwnerName = name, OwnerNameVerified = verified, OwnerNameConfidence = 0.9, OwnerNameSources = sources,
            Email = email, EmailStatus = es, EmailConfidence = email is null ? 0 : 0.45,
        };

    private static ListingReportRow Row(int id, string? website, ListingEnrichment? rollup, params FieldCandidate[] cands) =>
        new(new Listing { Id = id, Name = $"L{id}", Website = website, Vertical = "dentists", City = "Denver, CO" },
            rollup, cands);

    [Fact]
    public void Classify_RegistryOnly_WhenNoBaselineCandidate()
    {
        var row = Row(1, "https://x.com", Rollup("Kari Miller", true, "npi,state_license_board"),
            Owner("Kari Miller", EnrichmentSources.Npi), Owner("Kari Miller", EnrichmentSources.StateLicenseBoard));

        var c = CoverageReport.Classify(new[] { row }).Single();

        Assert.True(c.HasOwner);
        Assert.True(c.RegistryFound);
        Assert.False(c.BaselineFound);
        Assert.True(c.RegistryOnly);
        Assert.Equal("registry-only-lift", c.Classification);
    }

    [Fact]
    public void Classify_Baseline_WhenWebsiteFoundTheOwner()
    {
        var row = Row(2, "https://y.com", Rollup("Chelsea Mayer", true, "website,npi"),
            Owner("Chelsea Mayer", EnrichmentSources.Website), Owner("Chelsea Mayer", EnrichmentSources.Npi));

        var c = CoverageReport.Classify(new[] { row }).Single();

        Assert.True(c.BaselineFound);
        Assert.False(c.RegistryOnly);
        Assert.Equal("web/search", c.Classification);
    }

    [Fact]
    public void Aggregate_CountsLiftVerifiedAndFailures()
    {
        var rows = new[]
        {
            Row(1, "https://x.com", Rollup("Kari Miller", true, "npi,state_license_board"),
                Owner("Kari Miller", EnrichmentSources.Npi), Owner("Kari Miller", EnrichmentSources.StateLicenseBoard)),
            Row(2, "https://y.com", Rollup("Chelsea Mayer", true, "website,npi", "c@y.com", EmailVerificationStatus.Verified),
                Owner("Chelsea Mayer", EnrichmentSources.Website), Owner("Chelsea Mayer", EnrichmentSources.Npi)),
            Row(3, null, null),
        };

        var m = CoverageReport.Aggregate(CoverageReport.Classify(rows), "dentists", "Denver, CO");

        Assert.Equal(3, m.Total);
        Assert.Equal(2, m.NamedOwner);
        Assert.Equal(1, m.RegistryOnlyLift);
        Assert.Equal(1, m.BaselineOwner);
        Assert.Equal(2, m.VerifiedOwner);
        Assert.Equal(1, m.SmtpVerified);
        Assert.Equal(1, m.Failures["no website (and no registry match)"]);
        Assert.Equal(2, m.OwnerSourceContribution[EnrichmentSources.Npi]);  // both listings had an NPI owner candidate
    }
}
