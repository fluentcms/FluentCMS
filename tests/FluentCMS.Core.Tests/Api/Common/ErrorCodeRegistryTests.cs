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

    [Fact]
    public void EveryCodeWrittenInSourceIsListed()
    {
        var source = Path.Combine(RepositoryRoot(), "src");
        var used = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(source, path))
            .SelectMany(path => CodePattern().Matches(File.ReadAllText(path)).Select(match => (match.Value, Path.GetRelativePath(source, path))))
            .ToList();

        used.ShouldNotBeEmpty();
        used.Where(item => ErrorCodeRegistry.Find(item.Value) is null)
            .Select(item => $"{item.Value} in {item.Item2}")
            .Distinct()
            .ShouldBeEmpty();
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

    private static bool IsBuildOutput(string source, string path) =>
        Path.GetRelativePath(source, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

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
