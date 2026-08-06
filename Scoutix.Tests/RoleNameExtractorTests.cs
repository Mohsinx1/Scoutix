using Scoutix.Enrichment.Resolution;
using Scoutix.Enrichment.Sources.Website;

namespace Scoutix.Tests;

public class RoleNameExtractorTests
{
    private static RoleNameMatch? FindPerson(string text, string canonicalKey) =>
        RoleNameExtractor.Extract(text)
            .FirstOrDefault(m => NameNormalizer.CanonicalKey(m.Name) == canonicalKey);

    [Fact]
    public void Empty_ReturnsNothing()
    {
        Assert.Empty(RoleNameExtractor.Extract(""));
        Assert.Empty(RoleNameExtractor.Extract(null));
    }

    [Fact]
    public void NameThenRole_Owner_HighConfidence()
    {
        var m = FindPerson("Dr. Jane Smith, Owner & Lead Dentist", "jane smith");
        Assert.NotNull(m);
        Assert.Equal("Jane Smith", m!.Name);
        Assert.Equal(RoleNameExtractor.OwnerRoleConfidence, m.Confidence, 3);
    }

    [Fact]
    public void RoleThenName_Founder_HighConfidence()
    {
        var m = FindPerson("Our founder, Sarah Connor, opened the practice in 2004.", "sarah connor");
        Assert.NotNull(m);
        Assert.Equal(RoleNameExtractor.OwnerRoleConfidence, m!.Confidence, 3);
    }

    [Fact]
    public void RoleThenName_WithMiddleInitial()
    {
        var m = FindPerson("Owner: John A. Doe", "john doe");
        Assert.NotNull(m);
        Assert.Equal("John Doe", m!.Name);
    }

    [Fact]
    public void TitleOnly_LowerConfidence()
    {
        var m = FindPerson("Welcome to our office. Dr. Emily Carter has served the community for 20 years.", "emily carter");
        Assert.NotNull(m);
        Assert.Equal(RoleNameExtractor.TitleOnlyConfidence, m!.Confidence, 3);
    }

    [Fact]
    public void PracticeOwner_IsExecTier()
    {
        var m = FindPerson("Practice Owner: Dr. Maria Gomez", "maria gomez");
        Assert.NotNull(m);
        Assert.Equal(RoleNameExtractor.ExecRoleConfidence, m!.Confidence, 3);
    }

    [Fact]
    public void BusinessNamesAndHeadings_AreNotPeople()
    {
        var text = "Smile Dental Care — New Patients Welcome. Cosmetic Dentistry. Contact Us.";
        Assert.Empty(RoleNameExtractor.Extract(text));
    }

    [Fact]
    public void BareRole_WithoutName_YieldsNothing()
    {
        Assert.Empty(RoleNameExtractor.Extract("We are owner-operated and family owned."));
    }

    [Fact]
    public void TwoDoctors_BothFound()
    {
        var matches = RoleNameExtractor.Extract("Meet our team: Dr. Jane Smith and Dr. John Doe.");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void SamePerson_DeduplicatedToHighestConfidence()
    {
        // Title mention (0.45) + explicit owner cue (0.65) for the same person → one entry, the higher one.
        var matches = RoleNameExtractor.Extract("Dr. Jane Smith leads the team. Jane Smith, Owner, founded us.");
        var jane = matches.Where(m => NameNormalizer.CanonicalKey(m.Name) == "jane smith").ToList();
        Assert.Single(jane);
        Assert.Equal(RoleNameExtractor.OwnerRoleConfidence, jane[0].Confidence, 3);
    }

    [Fact]
    public void StripsHtmlTags_BeforeMatching()
    {
        var m = FindPerson("<div><p>Dr. <b>Jane</b> Smith, <span>Owner</span></p></div>", "jane smith");
        Assert.NotNull(m);
        Assert.Equal(RoleNameExtractor.OwnerRoleConfidence, m!.Confidence, 3);
    }

    [Fact]
    public void NewlineSeparatedTeamCard_IsMatched()
    {
        var m = FindPerson("Jane Smith\nOwner", "jane smith");
        Assert.NotNull(m);
        Assert.Equal(RoleNameExtractor.OwnerRoleConfidence, m!.Confidence, 3);
    }

    [Fact]
    public void RoleWord_IsNotSwallowedIntoName()
    {
        // "Dr. Chelsea Mayer Owner & Lead Dentist" must not yield a person named "...Owner".
        var matches = RoleNameExtractor.Extract("Dr. Chelsea Mayer Owner & Lead Dentist");
        Assert.All(matches, m => Assert.DoesNotContain("owner", m.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(matches, m => NameNormalizer.CanonicalKey(m.Name) == "chelsea mayer");
    }

    [Fact]
    public void Name_DoesNotSpanNewlineHeadings()
    {
        // Two stacked headings must not merge into "Mayer Lowry Neighborhood".
        var matches = RoleNameExtractor.Extract("Dr. Chelsea Mayer\nLowry Neighborhood Dentist");
        Assert.Single(matches);
        Assert.Equal("chelsea mayer", NameNormalizer.CanonicalKey(matches[0].Name));
    }
}
