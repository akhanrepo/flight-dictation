using Flight.Core.Vocabulary;
using Xunit;

namespace Flight.Tests.Vocabulary;

public class VocabularyTermRulesTests
{
    [Theory]
    [InlineData("Contoso")]
    [InlineData("SOW")]
    [InlineData("Fabrikam SKU")]
    [InlineData("Azure Front Door")]
    [InlineData("U.S.")]
    [InlineData("e.g")]
    [InlineData("kubernetes")]
    [InlineData("SAP S/4HANA")]
    public void TermRules_AcceptNamesAcronymsAndJargon(string term)
    {
        Assert.True(VocabularyTermRules.IsPlausibleTerm(term));
    }

    // The entry that leaked into every cleanup on Ali's machine
    [Theory]
    [InlineData("Let's see if it works. Let's see if it is working.")]
    [InlineData("Let's see if it works.")]
    [InlineData("we need to finish the security audit")]
    [InlineData("Are we done?")]
    [InlineData("first, update the SOW")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    public void TermRules_RejectSentencesAndNoise(string term)
    {
        Assert.False(VocabularyTermRules.IsPlausibleTerm(term));
    }

    [Fact]
    public void TermRules_RejectOverlongTerms()
    {
        Assert.False(VocabularyTermRules.IsPlausibleTerm(new string('x', VocabularyTermRules.MaxLength + 1)));
    }
}
