using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FluentCMS.Generators.Tests;

// Runs the generator on sources in memory, against the assemblies of the test process, which include the SDK and the
// media plugin. The assemblies that already hold generated builders are left out, so that only the sources count.
internal static class Generation
{
    private static readonly string[] Excluded = ["Blog.dll", "FluentCMS.Generators.Tests.dll"];

    public static ImmutableArray<MetadataReference> References { get; } =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !Excluded.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest);

    public static SyntaxTree Parse(string text, string path) =>
        CSharpSyntaxTree.ParseText(text, ParseOptions, path, cancellationToken: Xunit.TestContext.Current.CancellationToken);

    public static CSharpCompilation Compile(params (string Path, string Text)[] sources) =>
        CSharpCompilation.Create(
            "Host",
            sources.Select(source => Parse(source.Text, source.Path)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver Driver() =>
        CSharpGeneratorDriver.Create(
            [new FieldsGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public static GeneratorRunResult Run(CSharpCompilation compilation) =>
        Driver().RunGenerators(compilation).GetRunResult().Results.Single();

    public static string Generated(GeneratorRunResult result, string hintName) =>
        result.GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();

    // The generated sources compiled with the host's, and the errors of the whole.
    public static IReadOnlyList<Diagnostic> Errors(CSharpCompilation compilation)
    {
        Driver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return [.. output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}
