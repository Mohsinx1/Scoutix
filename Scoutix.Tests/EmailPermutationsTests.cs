using Scoutix.Enrichment.Email;

namespace Scoutix.Tests;

public class EmailPermutationsTests
{
    [Fact]
    public void Generate_ProducesStandardPatterns()
    {
        var perms = EmailPermutations.Generate("Jane", "Smith", "acmedental.com");

        Assert.Contains("jane.smith@acmedental.com", perms);
        Assert.Contains("jsmith@acmedental.com", perms);   // flast
        Assert.Contains("janes@acmedental.com", perms);    // firstl
        Assert.Contains("jane@acmedental.com", perms);
        Assert.Contains("smith@acmedental.com", perms);
        Assert.All(perms, p => Assert.EndsWith("@acmedental.com", p));
    }

    [Fact]
    public void Generate_OnFreeProvider_ReturnsEmpty()
        => Assert.Empty(EmailPermutations.Generate("Jane", "Smith", "gmail.com"));

    [Fact]
    public void Generate_NoName_ReturnsEmpty()
        => Assert.Empty(EmailPermutations.Generate("", "", "acmedental.com"));

    [Fact]
    public void Generate_NoDomain_ReturnsEmpty()
        => Assert.Empty(EmailPermutations.Generate("Jane", "Smith", null));

    [Fact]
    public void Generate_StripsAccentsAndPunctuationFromNames()
    {
        // "Dr." style noise shouldn't leak into the local part.
        var perms = EmailPermutations.Generate("Jane-Marie", "O'Smith", "acmedental.com");
        Assert.All(perms, p => Assert.DoesNotContain("'", p));
        Assert.All(perms, p => Assert.DoesNotContain("-", p.Split('@')[0]));
    }
}
