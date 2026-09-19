using Flight.Core.Vocabulary;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Vocabulary;

/// <summary>
/// Feature #2: extraction reuses the already-loaded LLM rather than a second
/// NLP pipeline, and copes with a small local model ignoring the output format.
/// </summary>
public class TermExtractionTests
{
    private readonly FakeLlmCompletion _llm = new();

    // Criterion: terms are extracted from the confirmed dictation's cleaned output
    [Fact]
    public async Task Extractor_Sends_CleanedOutput_To_The_Llm_And_Parses_Terms()
    {
        _llm.ResponseToReturn = "Contoso\nFabrikam\nSKU";

        var terms = await new LlmTermExtractor(_llm).ExtractAsync("We met Contoso about the Fabrikam SKU.");

        Assert.Equal("We met Contoso about the Fabrikam SKU.", _llm.LastRequest!.UserPrompt);
        Assert.Equal(["Contoso", "Fabrikam", "SKU"], terms);
    }

    // A local model will sometimes bullet or number its list despite the prompt
    [Fact]
    public void Parse_Strips_Bullets_Numbering_And_Quotes()
    {
        var terms = LlmTermExtractor.Parse("- Contoso\n2. Fabrikam\n* \"Northwind\"");

        Assert.Equal(["Contoso", "Fabrikam", "Northwind"], terms);
    }

    // Dedup within a single dictation, case-insensitively
    [Fact]
    public void Parse_Deduplicates_Terms_Case_Insensitively()
    {
        var terms = LlmTermExtractor.Parse("Contoso\ncontoso\nCONTOSO");

        Assert.Single(terms);
    }

    // Junk lines (blank, punctuation-only, overlong) never become vocabulary rows
    [Fact]
    public void Parse_Drops_NonTerm_Noise()
    {
        var terms = LlmTermExtractor.Parse($"\n---\n123\n{new string('x', 200)}\nContoso");

        Assert.Equal(["Contoso"], terms);
    }

    // "No such terms" is a normal answer, not a failure
    [Fact]
    public async Task EmptyResponse_Yields_No_Terms()
    {
        _llm.ResponseToReturn = "   ";

        var terms = await new LlmTermExtractor(_llm).ExtractAsync("Nothing notable here.");

        Assert.Empty(terms);
    }

    // No LLM call at all for an empty dictation
    [Fact]
    public async Task EmptyDictation_Does_Not_Call_The_Llm()
    {
        var terms = await new LlmTermExtractor(_llm).ExtractAsync("   ");

        Assert.Empty(terms);
        Assert.Empty(_llm.Requests);
    }
}
