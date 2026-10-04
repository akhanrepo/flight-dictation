using Flight.Core.Llm;
using Xunit;

namespace Flight.Tests.Llm;

/// <summary>Cleanup may remove and repunctuate, never add what wasn't said.</summary>
public class CleanupGuardTests
{
    private const string Raw =
        "um so the uh the client wants the the security audit finished by friday and um we still need access to their staging environment";

    [Fact]
    public void Guard_AcceptsANormalCleanup()
    {
        const string cleaned =
            "The client wants the security audit finished by Friday, and we still need access to their staging environment.";

        Assert.True(CleanupGuard.IsFaithful(Raw, cleaned, []));
    }

    // The exact regression: a stored "term" pasted in front of the real text
    [Fact]
    public void Guard_RejectsAnInventedSentence()
    {
        const string cleaned =
            "Let's see if it works. Let's see if it is working. The client wants the security audit finished by Friday, and we still need access to their staging environment.";

        Assert.False(CleanupGuard.IsFaithful(Raw, cleaned, []));
    }

    [Fact]
    public void Guard_RejectsAnAnswerInsteadOfAnEdit()
    {
        Assert.False(CleanupGuard.IsFaithful(
            "what time is the meeting tomorrow",
            "I don't have access to your calendar, but you can check Outlook for tomorrow's schedule.",
            []));
    }

    [Fact]
    public void Guard_AllowsAFewSmallFixes()
    {
        Assert.True(CleanupGuard.IsFaithful(
            "we're gonna send it over their tomorrow",
            "We're going to send it over there tomorrow.",
            []));
    }

    // Correcting to a learned spelling is the point of vocabulary context
    [Fact]
    public void Guard_AllowsLearnedVocabularySpellings()
    {
        Assert.True(CleanupGuard.IsFaithful(
            "um we met with contosoh and fabricam about the s k u",
            "We met with Contoso and Fabrikam about the SKU.",
            ["Contoso", "Fabrikam", "SKU"]));
    }

    [Fact]
    public void Guard_RejectsAnEmptyResultForRealSpeech()
    {
        Assert.False(CleanupGuard.IsFaithful("send the revised estimate", "", []));
    }

    [Fact]
    public void Fallback_RemovesFillersAndRepeatsAndTidiesTheEnds()
    {
        Assert.Equal(
            "So the client wants the security audit finished by friday and we still need access to their staging environment.",
            CleanupGuard.FallbackClean(Raw));
    }

    [Fact]
    public void Fallback_OfOnlyFillersIsEmpty()
    {
        Assert.Equal(string.Empty, CleanupGuard.FallbackClean("um uh erm"));
    }
}
