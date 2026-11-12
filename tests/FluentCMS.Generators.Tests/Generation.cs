using System.Collections.Immutable;
using FluentCMS.Core.Schema;
using FluentCMS.Generators.Emit;
using FluentCMS.Generators.Snapshot;
using FluentCMS.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

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

    // The implicit usings of an SDK project, which the sources of the sample and the kitchen rely on.
    public const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

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

    // The generator on the sources copied to a folder of the output, and the emitter on the snapshot of the host's schema
    // read back, give the same files with the same text.
    public static void ShouldEmitTheSameText(string sourceFolder, IServiceProvider host, string @namespace)
    {
        (string, string)[] sources =
        [
            .. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, sourceFolder), "*.cs").Select(path => (Path.GetFileName(path), File.ReadAllText(path))),
            ("ImplicitUsings.cs", ImplicitUsings),
        ];
        var fromSymbols = Run(Compile(sources));
        fromSymbols.Diagnostics.ShouldBeEmpty();

        var snapshot = SchemaJsonWriter.Write(host.GetRequiredService<SchemaModel>());
        var schema = new SchemaBuilder(host.GetRequiredService<IFieldTypeRegistry>()).AddSchemaJson(snapshot).Build();
        var fromSnapshot = FieldsEmitter.Emit(SchemaDescriptions.Of(schema, @namespace), EmitOptions.Sdk);

        fromSnapshot.Diagnostics.ShouldBeEmpty();
        fromSnapshot.Sources.Select(source => source.HintName).ShouldBe(fromSymbols.GeneratedSources.Select(source => source.HintName), ignoreOrder: true);
        foreach (var source in fromSnapshot.Sources)
        {
            source.Text.ShouldBe(Generated(fromSymbols, source.HintName), source.HintName);
        }
    }

    // The generated sources compiled with the host's, and the errors of the whole.
    public static IReadOnlyList<Diagnostic> Errors(CSharpCompilation compilation)
    {
        Driver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return [.. output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}
