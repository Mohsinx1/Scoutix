using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;
using Scoutix.Enrichment.Resolution;

namespace Scoutix.Tests;

public class OwnerNameResolverTests
{
    private static FieldCandidate Owner(string value, string source, double conf) =>
        new() { Field = EnrichmentField.OwnerName, Value = value, Source = source, Confidence = conf };

    [Fact]
    public void Resolve_NoCandidates_ReturnsNone()
    {
        var r = OwnerNameResolver.Resolve(Array.Empty<FieldCandidate>());
        Assert.False(r.HasValue);
        Assert.False(r.Verified);
    }

    [Fact]
    public void Resolve_SingleSource_NotVerified()
    {
        var r = OwnerNameResolver.Resolve(new[] { Owner("Jane Smith", EnrichmentSources.Website, 0.6) });
        Assert.Equal("Jane Smith", r.Value);
        Assert.False(r.Verified);
        Assert.Equal(0.6, r.Confidence, 3);
        Assert.Single(r.Sources);
    }

    [Fact]
    public void Resolve_TwoIndependentSourcesAgree_Verified()
    {
        var r = OwnerNameResolver.Resolve(new[]
        {
            Owner("Dr. Jane Smith, DDS", EnrichmentSources.Website, 0.6),
            Owner("JANE SMITH", EnrichmentSources.Npi, 0.9),
        });
        Assert.True(r.Verified);
        Assert.True(r.Confidence >= 0.9);
        Assert.Contains(EnrichmentSources.Npi, r.Sources);
        Assert.Contains(EnrichmentSources.Website, r.Sources);
    }

    [Fact]
    public void Resolve_SameSourceTwice_NotVerified()
    {
        // Two observations from the SAME source are not independent agreement.
        var r = OwnerNameResolver.Resolve(new[]
        {
            Owner("Jane Smith", EnrichmentSources.Website, 0.6),
            Owner("Jane Smith", EnrichmentSources.Website, 0.7),
        });
        Assert.False(r.Verified);
        Assert.Single(r.Sources);
    }

    [Fact]
    public void Resolve_RegistryAgreementBeatsLoneWebsiteName()
    {
        // The whole milestone: a registry pair outvotes a single unrelated website name.
        var r = OwnerNameResolver.Resolve(new[]
        {
            Owner("John Doe", EnrichmentSources.Website, 0.6),
            Owner("Jane Smith", EnrichmentSources.Npi, 0.9),
            Owner("Jane Smith", EnrichmentSources.StateLicenseBoard, 0.85),
        });
        Assert.Equal("Jane Smith", r.Value);
        Assert.True(r.Verified);
        Assert.Equal(2, r.Sources.Count);
    }
}
