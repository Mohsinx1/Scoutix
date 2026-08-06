using Scoutix.Enrichment.Sources.Npi;

namespace Scoutix.Tests;

public class NpiMatchingTests
{
    [Theory]
    [InlineData("303-698-0476", "3036980476")]
    [InlineData("+1 (303) 698-0476", "3036980476")]
    [InlineData("1-303-698-0476", "3036980476")]   // leading country code → last 10
    [InlineData("303", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DigitsOnly_NormalizesToLastTen(string? input, string? expected)
        => Assert.Equal(expected, NpiMatching.DigitsOnly(input));

    [Theory]
    [InlineData("Denver, CO", "CO")]
    [InlineData("Austin, TX", "TX")]
    [InlineData("Denver", null)]
    [InlineData("", null)]
    public void ParseState_PullsTwoLetterState(string input, string? expected)
        => Assert.Equal(expected, NpiMatching.ParseState(input));

    [Theory]
    [InlineData("Denver, CO", "Denver")]
    [InlineData("Colorado Springs, CO", "Colorado Springs")]
    public void ParseCity_PullsCity(string input, string expected)
        => Assert.Equal(expected, NpiMatching.ParseCity(input));

    [Fact]
    public void DeriveSurnames_PullsNameOutOfBusinessName()
        => Assert.Equal(new[] { "Miller" }, NpiMatching.DeriveSurnames("Miller Family Dental"));

    [Fact]
    public void DeriveSurnames_AllBusinessWords_ReturnsEmpty()
        => Assert.Empty(NpiMatching.DeriveSurnames("Smile Dental Care"));

    [Fact]
    public void DeriveSurnames_NullOrBlank_ReturnsEmpty()
        => Assert.Empty(NpiMatching.DeriveSurnames(null));

    [Theory]
    [InlineData("Miller Family Dental PC", "Miller Family Dental")]
    [InlineData("City Smiles LLC", "City Smiles")]
    [InlineData("Southeast Denver Dental", "Southeast Denver Dental")]
    public void CleanOrgName_TrimsLegalSuffixes(string input, string expected)
        => Assert.Equal(expected, NpiMatching.CleanOrgName(input));
}
