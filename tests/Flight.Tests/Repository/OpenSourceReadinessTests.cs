using System.Text.Json;
using Xunit;

namespace Flight.Tests.Repository;

/// <summary>Feature #11: the repository is ready to be public.</summary>
public class OpenSourceReadinessTests
{
    // Criterion: the files a stranger expects in an open-source project are there
    [Theory]
    [InlineData("README.md")]
    [InlineData("LICENSE")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("CODE_OF_CONDUCT.md")]
    [InlineData("SECURITY.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData("THIRD_PARTY_NOTICES.md")]
    [InlineData("docs/architecture.md")]
    [InlineData(".github/pull_request_template.md")]
    [InlineData(".github/ISSUE_TEMPLATE/bug_report.yml")]
    [InlineData(".github/ISSUE_TEMPLATE/feature_request.yml")]
    [InlineData(".github/dependabot.yml")]
    [InlineData(".github/workflows/codeql.yml")]
    public void Repository_HasTheStandardProjectFiles(string path)
    {
        Assert.True(File.Exists(RepoPaths.Combine(path.Split('/'))), $"{path} is missing");
    }

    [Fact]
    public void Repository_IsMitLicensed()
    {
        Assert.StartsWith("MIT License", File.ReadAllText(RepoPaths.Combine("LICENSE")));
    }

    // Criterion: no account, billing or backend code remains
    [Fact]
    public void Repository_HasNoPaywallBackend()
    {
        Assert.False(Directory.Exists(RepoPaths.Combine("supabase")));
        Assert.False(Directory.Exists(RepoPaths.Combine("src", "Flight.Core", "Entitlement")));
        Assert.False(Directory.Exists(RepoPaths.Combine("src", "Flight.Core", "Billing")));
        Assert.False(Directory.Exists(RepoPaths.Combine("src", "Flight.Core", "Accounts")));
        Assert.False(File.Exists(RepoPaths.Combine(".github", "workflows", "backend-deploy.yml")));
    }

    // Criterion: the shipped settings hold no personal paths or backend URLs
    [Fact]
    public void ShippedAppSettings_HoldNothingPersonal()
    {
        var text = File.ReadAllText(RepoPaths.Combine("src", "Flight.App", "appsettings.json"));
        using var json = JsonDocument.Parse(text);

        Assert.DoesNotContain(":/", text.Replace("https://", string.Empty));
        Assert.DoesNotContain("supabase", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "https://github.com/akhanrepo/flight-dictation",
            json.RootElement.GetProperty("Flight").GetProperty("UpdateFeed").GetString());
    }

    // Criterion: CI runs the Core tests on Linux as well as Windows
    [Fact]
    public void Ci_TestsOnLinuxAndWindows()
    {
        var ci = File.ReadAllText(RepoPaths.Combine(".github", "workflows", "ci.yml"));

        Assert.Contains("ubuntu-latest", ci);
        Assert.Contains("windows-latest", ci);
    }

    // Criterion: a tag publishes a GitHub Release
    [Fact]
    public void Release_PublishesToGitHubReleases()
    {
        var release = File.ReadAllText(RepoPaths.Combine(".github", "workflows", "release.yml"));

        Assert.Contains("vpk upload github", release);
        Assert.Contains("contents: write", release);
    }
}
