using System.Reflection;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public partial class ErrorCodeRegistryTests
{
    [GeneratedRegex(@"FCMS\d{4}")]
    private static partial Regex CodePattern();

    [Fact]
    public void EveryCodeIsListedOnce()
    {
        var duplicates = ErrorCodeRegistry.All.GroupBy(info => info.Code).Where(group => group.Count() > 1).Select(group => group.Key);

        duplicates.ShouldBeEmpty();
    }

    [Fact]
    public void EveryEntryHasFourDigitsAnErrorStatusAndATitle()
    {
        foreach (var info in ErrorCodeRegistry.All)
        {
            CodePattern().IsMatch(info.Code).ShouldBeTrue(info.Code);
            info.Code.Length.ShouldBe(8, info.Code);
            info.Status.ShouldBeInRange(400, 599, info.Code);
            info.Title.ShouldNotBeNullOrWhiteSpace(info.Code);
        }
    }

    [Fact]
    public void EveryCodeOfAnErrorCodeClassIsListed()
    {
        var declared = new[] { typeof(ErrorCodeRegistry).Assembly, typeof(ValidationErrorCodes).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("Codes", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => CodePattern().IsMatch(value))
            .ToList();

        declared.ShouldNotBeEmpty();
        declared.Where(code => ErrorCodeRegistry.Find(code) is null).ShouldBeEmpty();
    }

    // A code written under src/plugins/{project} is listed when core has it or when that plugin adds it with AddErrorCode.
    // The generator and the analyzers report compiler diagnostics, not problems a client sees, so their codes are not listed.
    [Fact]
    public void EveryCodeWrittenInSourceIsListed()
    {
        var source = Path.Combine(RepositoryRoot(), "src");
        var used = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(source, path) && !IsCompileTime(source, path))
            .SelectMany(path => CodePattern().Matches(File.ReadAllText(path)).Select(match => (match.Value, Path.GetRelativePath(source, path))))
            .ToList();
        var plugins = Directory.EnumerateDirectories(Path.Combine(source, "plugins"))
            .ToDictionary(directory => Path.GetFileName(directory), CodesAddedBy, StringComparer.Ordinal);

        used.ShouldNotBeEmpty();
        used.Where(item => ErrorCodeRegistry.Find(item.Value) is null && !(PluginOf(item.Item2) is { } plugin && plugins[plugin].Contains(item.Value)))
            .Select(item => $"{item.Value} in {item.Item2}")
            .Distinct()
            .ShouldBeEmpty();
    }

    [Fact]
    public void TheCodesAPluginAddsAreFoundInItsSource()
    {
        var webhooks = CodesAddedBy(Path.Combine(RepositoryRoot(), "src", "plugins", "FluentCMS.Plugins.Webhooks"));

        webhooks.ShouldContain("FCMS1560");
        webhooks.ShouldNotContain(code => ErrorCodeRegistry.Find(code) != null);
    }

    [Theory]
    [InlineData("FCMS2301", 404)]
    [InlineData("FCMS2808", 412)]
    [InlineData("FCMS2401", 409)]
    [InlineData("FCMS2101", 422)]
    [InlineData("FCMS2202", 400)]
    [InlineData("FCMS1201", 500)]
    public void ACodeHasTheStatusOfItsMeaning(string code, int status)
    {
        ErrorCodeRegistry.Find(code)!.Status.ShouldBe(status);
    }

    [Fact]
    public void AnUnknownCodeIsNotFound()
    {
        ErrorCodeRegistry.Find("ACME0001").ShouldBeNull();
        ErrorCodeRegistry.Find(null).ShouldBeNull();
    }

    private static string? PluginOf(string relative) =>
        relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is ["plugins", var project, _, ..] ? project : null;

    // The codes of AddErrorCode calls, written as a literal or as a constant of the project.
    private static HashSet<string> CodesAddedBy(string project)
    {
        var text = string.Join('\n', Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(project, path))
            .Select(File.ReadAllText));
        var constants = ConstantPattern().Matches(text).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["code"].Value, StringComparer.Ordinal);
        return [.. AddErrorCodePattern().Matches(text)
            .Select(match => match.Groups["code"].Success ? match.Groups["code"].Value : constants.GetValueOrDefault(match.Groups["name"].Value))
            .OfType<string>()];
    }

    [GeneratedRegex("""(?<name>\w+)\s*=\s*"(?<code>FCMS\d{4})"\s*;""")]
    private static partial Regex ConstantPattern();

    [GeneratedRegex("""AddErrorCode\(\s*(?:"(?<code>FCMS\d{4})"|(?:\w+\.)*(?<name>\w+)\s*,)""")]
    private static partial Regex AddErrorCodePattern();

    private static bool IsBuildOutput(string source, string path) =>
        Path.GetRelativePath(source, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static bool IsCompileTime(string source, string path) =>
        Path.GetRelativePath(source, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] is "FluentCMS.Generators" or "FluentCMS.Analyzers";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
