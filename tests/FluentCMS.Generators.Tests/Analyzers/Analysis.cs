extern alias Analyzers;

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Analyzers::FluentCMS.Analyzers;
using FluentCMS.Core.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Generators.Tests.Analyzers;

// Holds the analyzers to schema validation: a source a rule reports is refused at start with the code its message names,
// and a source the analyzers accept builds a SchemaModel.
internal static partial class Analysis
{
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using FluentCMS.Content;
        using FluentCMS.Publishing;
        using FluentCMS.Schema;

        """;

    // The marked spans are reported, each by its rule with the rule's severity, every message names the code, and schema
    // validation refuses the source with it.
    public static async Task Reports(string markup, string schemaCode)
    {
        await Verify<SchemaAttributeAnalyzer>(Usings + markup);

        var source = Unmark(Usings + markup);
        var diagnostics = await Diagnostics(source);
        diagnostics.ShouldNotBeEmpty();
        diagnostics.ShouldAllBe(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(schemaCode, StringComparison.Ordinal));

        SchemaErrors(source).Select(error => error.Code).ShouldContain(schemaCode);
    }

    // Nothing is reported and the schema builds.
    public static async Task Accepts(string source)
    {
        await Verify<SchemaAttributeAnalyzer>(Usings + source);

        (await Diagnostics(Usings + source)).ShouldBeEmpty();
        SchemaErrors(Usings + source).ShouldBeEmpty();
    }

    public static async Task Verify<TAnalyzer>(string markup)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
            TestCode = markup,
        };
        test.TestState.AdditionalReferences.AddRange(Generation.References);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    public static ImmutableArray<DiagnosticAnalyzer> All { get; } = [new SchemaAttributeAnalyzer(), new FilterIndexAnalyzer()];

    public static Task<ImmutableArray<Diagnostic>> Diagnostics(string source) =>
        Diagnostics(Generation.Compile(("Source.cs", source)));

    public static Task<ImmutableArray<Diagnostic>> Diagnostics(Compilation compilation) =>
        compilation.WithAnalyzers(All).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

    // The errors schema validation gives the classes of the source, read from the compiled assembly as a host reads its own.
    public static IReadOnlyList<SchemaError> SchemaErrors(string source)
    {
        using var stream = new MemoryStream();
        var emitted = Generation.Compile(("Source.cs", source)).Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();

        stream.Position = 0;
        var context = new AssemblyLoadContext("Schema", isCollectible: true);
        try
        {
            new SchemaBuilder().AddContentTypesFromAssembly(context.LoadFromStream(stream)).Build();
            return [];
        }
        catch (SchemaValidationException exception)
        {
            return exception.Errors;
        }
        finally
        {
            context.Unload();
        }
    }

    public static string Unmark(string markup) => Marker().Replace(markup, "").Replace("|}", "", StringComparison.Ordinal);

    [GeneratedRegex(@"\{\|FCMS\d{4}:")]
    private static partial Regex Marker();
}
