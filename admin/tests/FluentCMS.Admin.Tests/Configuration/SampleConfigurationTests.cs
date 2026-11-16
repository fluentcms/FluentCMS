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

    [Fact]
    public void TheSampleIsValidAndHasNoUnknownMember()
    {
        var startup = AdminConfigurationReader.Read(Sample);

        startup.Problems.ShouldBeEmpty();
        startup.UnknownMembers.ShouldBeEmpty();
        startup.Configuration.ShouldNotBeNull().ApiBaseUrl.ShouldBe(new Uri("http://localhost:8080/"));
    }

    [Fact]
    public void TheSampleHasEveryMemberOfTheSpecification()
    {
        var spec = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "planning", "admin-technical-spec.md"));
        var start = spec.IndexOf("### 2.5 ", StringComparison.Ordinal);
        var section = spec[start..spec.IndexOf("### 2.6 ", start, StringComparison.Ordinal)];
        var specMembers = TableMember().Matches(section).Select(match => match.Groups[1].Value);

        using var sample = JsonDocument.Parse(Sample);
        var sampleMembers = sample.RootElement.EnumerateObject().Select(property => property.Name);

        sampleMembers.ShouldBe(specMembers);
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

    [GeneratedRegex(@"^\| `(\w+)` \|", RegexOptions.Multiline)]
    private static partial Regex TableMember();
}
