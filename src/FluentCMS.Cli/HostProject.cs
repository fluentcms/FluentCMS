using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Cli;

// The host's built assembly, loaded next to the CLI's own copies of FluentCMS.Core and EF Core, so that both sides see
// one CmsDbContext. Whatever else the host needs, such as its data provider, resolves through its deps.json.
internal sealed class HostProject : IDisposable
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Assembly _assembly;
    private readonly string _rootNamespace;
    private readonly string _directory;

    private HostProject(string assemblyPath, string rootNamespace, string directory)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _rootNamespace = rootNamespace;
        _directory = directory;

        AssemblyLoadContext.Default.Resolving += ResolveAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNativeLibrary;
        _assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    public static HostProject Open(string? project, string? assembly, TextWriter output)
    {
        if (assembly is not null)
        {
            var path = Path.GetFullPath(assembly);
            return new HostProject(path, Path.GetFileNameWithoutExtension(path), Path.GetFullPath(project ?? "."));
        }

        var target = project ?? ".";
        output.WriteLine($"Building {target}");
        RunDotnet("build", target);

        using var json = JsonDocument.Parse(RunDotnet("msbuild", target, "-getProperty:TargetPath", "-getProperty:RootNamespace", "-getProperty:MSBuildProjectDirectory"));
        var properties = json.RootElement.GetProperty("Properties");
        return new HostProject(
            properties.GetProperty("TargetPath").GetString()!,
            properties.GetProperty("RootNamespace").GetString()!,
            properties.GetProperty("MSBuildProjectDirectory").GetString()!);
    }

    public IReadOnlyList<string> AddMigration(string name)
    {
        var factories = _assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDesignTimeDbContextFactory<CmsDbContext>).IsAssignableFrom(type))
            .ToList();

        if (factories.Count != 1)
        {
            throw new InvalidOperationException(
                $"{_assembly.GetName().Name} has {factories.Count} implementations of IDesignTimeDbContextFactory<CmsDbContext>; it needs exactly one, which returns ContentMigrations.CreateContext(...).");
        }

        var factory = (IDesignTimeDbContextFactory<CmsDbContext>)Activator.CreateInstance(factories[0])!;
        var files = ContentMigrationGenerator.Add(() => factory.CreateDbContext([]), name, _rootNamespace, _directory);
        return [files.MigrationFile, files.MetadataFile, files.SnapshotFile];
    }

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveNativeLibrary;
    }

    private Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name) =>
        _resolver.ResolveAssemblyToPath(name) is { } path ? context.LoadFromAssemblyPath(path) : null;

    private IntPtr ResolveNativeLibrary(Assembly assembly, string name) =>
        _resolver.ResolveUnmanagedDllToPath(name) is { } path ? NativeLibrary.Load(path) : IntPtr.Zero;

    private static string RunDotnet(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"dotnet {arguments[0]} failed:{Environment.NewLine}{output}{errors.GetAwaiter().GetResult()}");
    }
}
