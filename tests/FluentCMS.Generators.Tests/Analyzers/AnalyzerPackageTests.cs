extern alias Analyzers;

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using AnalyzerStorage = Analyzers::FluentCMS.Analyzers.StorageNames;
using CoreStorage = FluentCMS.Core.Storage.StorageNames;

namespace FluentCMS.Generators.Tests.Analyzers;

public sealed partial class AnalyzerPackageTests
{
    private static IEnumerable<DiagnosticDescriptor> Rules =>
        Analysis.All.SelectMany(analyzer => analyzer.SupportedDiagnostics);

    [Fact]
    public void TheRulesAreFcms0001ToFcms0099AndEachHasItsHelpPage()
    {
        var reference = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "analyzers.md"));
        var pages = ReferenceHeading().Matches(reference).Select(match => match.Groups["code"].Value).ToList();

        Rules.Select(rule => rule.Id).ShouldBeUnique();
        foreach (var rule in Rules)
        {
            RuleCode().IsMatch(rule.Id).ShouldBeTrue(rule.Id);
            rule.HelpLinkUri.ShouldBe($"https://github.com/fluentcms/FluentCMS/blob/main/docs/analyzers.md#{rule.Id.ToLowerInvariant()}");
            rule.IsEnabledByDefault.ShouldBeTrue(rule.Id);
        }

        pages.ShouldBe(Rules.Select(rule => rule.Id).Order(StringComparer.Ordinal), ignoreOrder: false);
    }

    [Fact]
    public void EachRuleHasTheSeverityOfTheIssue()
    {
        foreach (var rule in Rules)
        {
            var expected = rule.Id switch
            {
                "FCMS0050" => DiagnosticSeverity.Warning,
                "FCMS0060" => DiagnosticSeverity.Info,
                _ => DiagnosticSeverity.Error,
            };
            rule.DefaultSeverity.ShouldBe(expected, rule.Id);
        }
    }

    [Fact]
    public void EachMessageNamesTheCodeOfSchemaValidation()
    {
        foreach (var rule in Rules.Where(rule => rule.Id != "FCMS0060"))
        {
            SchemaCode().IsMatch(rule.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture)).ShouldBeTrue(rule.Id);
        }
    }

    // The names and limits the analyzers check against are those of schema validation.
    [Fact]
    public void TheNamesAndLimitsAreThoseOfSchemaValidation()
    {
        AnalyzerStorage.ReservedFieldNames.ShouldBe(FluentCMS.Schema.ContentItem.ReservedFieldNames, ignoreOrder: true);
        AnalyzerStorage.SystemTables.ShouldBe(CoreStorage.SystemTables, ignoreOrder: true);
        AnalyzerStorage.SystemColumns.ShouldBe(CoreStorage.SystemColumns, ignoreOrder: true);
        AnalyzerStorage.LocaleTableColumns.ShouldBe(CoreStorage.LocaleTableColumns, ignoreOrder: true);
        AnalyzerStorage.Prefix.ShouldBe(CoreStorage.Prefix);
        AnalyzerStorage.NameLength.ShouldBe(CoreStorage.NameLength);
        AnalyzerStorage.MaxIdentifierLength.ShouldBe(CoreStorage.MaxIdentifierLength);
        AnalyzerStorage.LocaleTable("site-host").ShouldBe(CoreStorage.LocaleTable("site-host"));
    }

    [Theory]
    [InlineData("article")]
    [InlineData("site-settings")]
    [InlineData("a1")]
    [InlineData("")]
    [InlineData("Article")]
    [InlineData("a--b")]
    [InlineData("a-")]
    [InlineData("1a")]
    public void TypeNamesAreValidatedAsTheHostDoes(string name) =>
        AnalyzerStorage.IsValidTypeName(name).ShouldBe(IsValidTypeName(name));

    [Theory]
    [InlineData("title")]
    [InlineData("publishedOn")]
    [InlineData("seo2")]
    [InlineData("")]
    [InlineData("Title")]
    [InlineData("my_field")]
    public void FieldNamesAreValidatedAsTheHostDoes(string name) =>
        AnalyzerStorage.IsValidFieldName(name).ShouldBe(IsValidFieldName(name));

    [Theory]
    [InlineData("publishedOn")]
    [InlineData("siteId")]
    [InlineData("seo2Title")]
    [InlineData("alternativeTitleForSocialMediaSharingCard")]
    public void ColumnsAreNamedAsTheHostNamesThem(string field) =>
        AnalyzerStorage.Column(field).ShouldBe(CoreStorage.Column(field));

    // The sample and the kitchen of the generator are valid schemas, and the analyzers say nothing about them.
    [Theory]
    [InlineData("BlogSources")]
    [InlineData("KitchenSources")]
    public async Task TheAnalyzersAreSilentOnTheSampleAndTheKitchen(string folder)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, folder), "*.cs")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            .Append(("ImplicitUsings.cs", Generation.ImplicitUsings))
            .ToArray();
        Generation.Driver().RunGeneratorsAndUpdateCompilation(Generation.Compile(sources), out var compilation, out _, TestContext.Current.CancellationToken);

        (await Analysis.Diagnostics(compilation)).ShouldBeEmpty();
    }

    // Every .NET 10 SDK from the one global.json pins (10.0.100) has the compiler of Roslyn 5.0, so the analyzers, built on
    // netstandard2.0 against Microsoft.CodeAnalysis 5.0.0, load in it; a later Roslyn would not load in that SDK.
    [Fact]
    public void TheAnalyzersLoadInTheSdkThatGlobalJsonPins()
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "global.json")));
        var sdk = Version.Parse(globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!);
        sdk.ShouldBe(new Version(10, 0, 100));

        var assembly = typeof(Analyzers::FluentCMS.Analyzers.SchemaAttributeAnalyzer).Assembly;
        var roslyn = assembly.GetReferencedAssemblies().Single(reference => reference.Name == "Microsoft.CodeAnalysis");
        roslyn.Version.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(new Version(5, 0, 0, 0));
        assembly.GetReferencedAssemblies().ShouldContain(reference => reference.Name == "netstandard");
    }

    private static bool IsValidTypeName(string name) =>
        (bool)typeof(FluentCMS.Schema.ContentItem).Assembly.GetType("FluentCMS.Schema.SchemaNaming")!.GetMethod("IsValidTypeName")!.Invoke(null, [name])!;

    private static bool IsValidFieldName(string name) =>
        (bool)typeof(FluentCMS.Schema.ContentItem).Assembly.GetType("FluentCMS.Schema.SchemaNaming")!.GetMethod("IsValidFieldName")!.Invoke(null, [name])!;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [GeneratedRegex(@"^FCMS00(0[1-9]|[1-9]\d)$")]
    private static partial Regex RuleCode();

    [GeneratedRegex(@"FCMS12\d{2}")]
    private static partial Regex SchemaCode();

    [GeneratedRegex(@"^### (?<code>FCMS\d{4})$", RegexOptions.Multiline)]
    private static partial Regex ReferenceHeading();
}
