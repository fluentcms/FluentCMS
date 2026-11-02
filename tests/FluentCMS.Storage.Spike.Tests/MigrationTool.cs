using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Storage.Spike.Tests;

// The in-process equivalent of `dotnet ef migrations add`, which is what `fluentcms migrations add` needs.
internal static class MigrationTool
{
    public static ScaffoldedMigration Scaffold(DbContext context, string name)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDesignTimeServices();
        services.AddDbContextDesignTimeServices(context);

        var providerAssembly = Assembly.Load(new AssemblyName(context.GetService<IDatabaseProvider>().Name));
        var attribute = providerAssembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>()!;
        var designTimeServices = providerAssembly.GetType(attribute.TypeName, throwOnError: true)!;
        ((IDesignTimeServices)Activator.CreateInstance(designTimeServices)!).ConfigureDesignTimeServices(services);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration(name, "FluentCMS.Generated");
    }

    public static Assembly Compile(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            $"FluentCMS.Generated.{Guid.NewGuid():N}",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString());
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
