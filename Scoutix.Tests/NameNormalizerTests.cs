using Scoutix.Enrichment.Resolution;

namespace Scoutix.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Dr. Jane A. Smith, DDS", "jane smith")]
    [InlineData("JANE SMITH", "jane smith")]
    [InlineData("Smith, Jane", "jane smith")]
    [InlineData("Jane Smith DMD", "jane smith")]
    [InlineData("  Jane   Smith  ", "jane smith")]
    public void CanonicalKey_NormalizesToFirstLast(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.CanonicalKey(input));

    [Fact]
    public void CanonicalKey_Blank_ReturnsNull()
        => Assert.Null(NameNormalizer.CanonicalKey("   "));

    [Theory]
    [InlineData("Dr. Jane A. Smith, DDS", "JANE SMITH", true)]   // titles/credentials/middle-initial ignored
    [InlineData("Jane Smith", "John Smith", false)]
    [InlineData("Jane Smith", "Jane Doe", false)]
    public void Match_RecognisesSamePerson(string a, string b, bool expected)
        => Assert.Equal(expected, NameNormalizer.Match(a, b));

    [Fact]
    public void Clean_StripsTitlesAndCredentials()
        => Assert.Equal("Jane Smith", NameNormalizer.Clean("Dr. Jane Smith DDS"));
}
