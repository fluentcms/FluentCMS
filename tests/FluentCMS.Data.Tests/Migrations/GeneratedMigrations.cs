using System.Reflection;
using System.Runtime.Loader;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FluentCMS.Data.Tests.Migrations;

// Generated content migrations, compiled the way a host project would and loaded as its migrations assembly.
internal static class GeneratedMigrations
{
    public const string Namespace = "FluentCMS.Generated";

    public static GeneratedMigration Scaffold(SchemaModel schema, string name, Assembly? previous = null) =>
        ContentMigrationGenerator.Scaffold(
            () => ContentMigrations.CreateContext(new SqliteDataProvider("Data Source=:memory:"), schema, previous ?? typeof(CmsDbContext).Assembly),
            name,
            Namespace);

    public static Assembly Compile(params IEnumerable<string> sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            $"{Namespace}.{Guid.NewGuid():N}",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }

        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    public static Assembly CompileFirst(SchemaModel schema, string name = "Initial")
    {
        var migration = Scaffold(schema, name);
        return Compile(migration.MigrationCode, migration.MetadataCode, migration.SnapshotCode);
    }
}
