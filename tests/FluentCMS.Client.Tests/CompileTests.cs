using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace FluentCMS.Client.Tests;

public sealed class CompileTests
{
    // The code `fluentcms generate csharp` wrote for the Blog sample compiles on its own against FluentCMS.Client, with
    // nullable references on and every warning an error.
    [Fact]
    public void TheGeneratedBlogCodeCompilesWithWarningsAsErrors()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "BlogSources"), "*.g.cs")
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToList();
        sources.Count.ShouldBeGreaterThan(10);

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Append(typeof(FluentCmsClient).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Blog.Client",
            sources,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error, warningLevel: 9999));

        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: TestContext.Current.CancellationToken);

        result.Diagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).ShouldBeEmpty();
        result.Success.ShouldBeTrue();
    }
}
