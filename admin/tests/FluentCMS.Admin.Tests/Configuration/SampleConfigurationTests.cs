using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Admin.Configuration;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed partial class SampleConfigurationTests
{
    private static readonly string Sample = File.ReadAllText(
        Path.Combine(RepositoryRoot(), "admin", "src", "FluentCMS.Admin.App", "wwwroot", "config.json"));

    private static readonly string[] TableRows = SpecTableRows();

    [Fact]
    public void TheSampleIsValidAndHasNoUnknownMember()
    {
        var startup = AdminConfigurationReader.Read(Sample);

        startup.Problems.ShouldBeEmpty();
        startup.UnknownMembers.ShouldBeEmpty();
    }

    [Fact]
    public void TheSampleReadsAsTheDefaults()
    {
        var sample = AdminConfigurationReader.Read(Sample).Configuration.ShouldNotBeNull();
        var defaults = new AdminConfiguration { ApiBaseUrl = sample.ApiBaseUrl };

        sample.ApiBaseUrl.ShouldBe(new Uri("http://localhost:8080/"));
        sample.Authority.OriginalString.ShouldBe("http://localhost:8080");
        sample.ClientId.ShouldBe(defaults.ClientId);
        sample.Scopes.ShouldBe(defaults.Scopes);
        sample.Branding.ShouldBe(defaults.Branding);
        sample.Cultures.Offered.ShouldBe(defaults.Cultures.Offered);
        sample.Cultures.Default.ShouldBe(defaults.Cultures.Default);
        sample.PreviewOrigins.ShouldBe(defaults.PreviewOrigins);
    }

    [Fact]
    public void TheSampleHasEveryMemberOfTheSpecification()
    {
        using var sample = JsonDocument.Parse(Sample);

        sample.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(TableRows.Select(row => TableMember().Match(row).Groups[1].Value));
    }

    [Theory]
    [InlineData("branding")]
    [InlineData("cultures")]
    public void TheNestedMembersOfTheSampleAreThoseOfTheSpecification(string member)
    {
        using var sample = JsonDocument.Parse(Sample);
        var row = TableRows.Single(row => row.StartsWith($"| `{member}` |", StringComparison.Ordinal));

        // The backticked names of the row's meaning, without the URL schemes it also mentions.
        var meaning = row.Split('|')[2];
        var specMembers = BacktickedName().Matches(meaning).Select(match => match.Groups[1].Value).Where(name => name is not "http" and not "https");

        sample.RootElement.GetProperty(member).EnumerateObject().Select(property => property.Name).ShouldBe(specMembers);
    }

    private static string[] SpecTableRows()
    {
        var spec = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "planning", "admin-technical-spec.md"));
        var start = spec.IndexOf("### 2.5 ", StringComparison.Ordinal);
        var section = spec[start..spec.IndexOf("### 2.6 ", start, StringComparison.Ordinal)];
        return section.Split('\n').Where(line => TableMember().IsMatch(line)).ToArray();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FluentCMS.slnx was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^\| `(\w+)` \|")]
    private static partial Regex TableMember();

    [GeneratedRegex(@"`([a-z][A-Za-z]*)`")]
    private static partial Regex BacktickedName();
}
