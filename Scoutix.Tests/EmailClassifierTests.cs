using Scoutix.Enrichment.Email;

namespace Scoutix.Tests;

public class EmailClassifierTests
{
    [Theory]
    [InlineData("jane@acmedental.com", true)]
    [InlineData("jane.smith@acme.co.uk", true)]
    [InlineData("not-an-email", false)]
    [InlineData("a@b", false)]
    [InlineData("", false)]
    public void IsValidSyntax(string email, bool expected)
        => Assert.Equal(expected, EmailClassifier.IsValidSyntax(email));

    [Theory]
    [InlineData("jane@AcmeDental.com", "acmedental.com")]
    [InlineData("info@sub.acme.com", "sub.acme.com")]
    public void GetDomain(string email, string expected)
        => Assert.Equal(expected, EmailClassifier.GetDomain(email));

    [Theory]
    [InlineData("info@acme.com", true)]
    [InlineData("contact@acme.com", true)]
    [InlineData("jane@acme.com", false)]
    public void IsRoleAddress(string email, bool expected)
        => Assert.Equal(expected, EmailClassifier.IsRoleAddress(email));

    [Theory]
    [InlineData("x@mailinator.com", true)]
    [InlineData("x@acme.com", false)]
    public void IsDisposableDomain(string email, bool expected)
        => Assert.Equal(expected, EmailClassifier.IsDisposableDomain(email));

    [Theory]
    [InlineData("jane@gmail.com", true)]
    [InlineData("jane@acmedental.com", false)]
    public void IsFreeProvider(string email, bool expected)
        => Assert.Equal(expected, EmailClassifier.IsFreeProvider(email));
}
