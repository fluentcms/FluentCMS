using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Cli;

// The host's built assembly, loaded next to the CLI's own copies of FluentCMS.Core and EF Core, so that both sides see
// one CmsDbContext. Whatever else the host needs, such as its data provider, resolves through its deps.json.
internal sealed class HostProject : IDisposable
{
    private const string CoreName = "FluentCMS.Core";

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

    public static HostProject Open(HostTarget target) => new(target.AssemblyPath, target.RootNamespace, target.ProjectDirectory);

    public IReadOnlyList<string> AddMigration(string name) => WithFactory(factory =>
    {
        var files = ContentMigrationGenerator.Add(() => factory.CreateDbContext([]), name, _rootNamespace, _directory);
        return (IReadOnlyList<string>)[files.MigrationFile, files.MetadataFile, files.SnapshotFile];
    });

    public SchemaDiffReport? ReviewMigration() => WithFactory(factory => ContentMigrationGenerator.Review(() => factory.CreateDbContext([])));

    public string? NewestMigration() => WithFactory(factory => ContentMigrationGenerator.Newest(() => factory.CreateDbContext([])));

    public RemovedMigrationFiles RemoveMigration() =>
        WithFactory(factory => ContentMigrationGenerator.Remove(() => factory.CreateDbContext([]), _rootNamespace, _directory));

    private T WithFactory<T>(Func<IDesignTimeDbContextFactory<CmsDbContext>, T> use)
    {
        try
        {
            var factories = _assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDesignTimeDbContextFactory<CmsDbContext>).IsAssignableFrom(type))
                .ToList();

            if (factories.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{_assembly.GetName().Name} has {factories.Count} implementations of IDesignTimeDbContextFactory<CmsDbContext>; it needs exactly one, which returns ContentMigrations.CreateContext(...).");
            }

            return use((IDesignTimeDbContextFactory<CmsDbContext>)Activator.CreateInstance(factories[0])!);
        }
        catch (Exception exception) when (exception is ReflectionTypeLoadException or TargetInvocationException or TypeLoadException or MissingMemberException or FileLoadException)
        {
            throw new InvalidOperationException(LoadFailure(exception), exception);
        }
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

    // The host binds to this tool's FluentCMS.Core and EF Core, so a host built against other versions of them fails with
    // a type or a member that this tool's copies do not have.
    private string LoadFailure(Exception exception)
    {
        var cause = exception switch
        {
            ReflectionTypeLoadException load => load.LoaderExceptions.FirstOrDefault(inner => inner is not null) ?? load,
            TargetInvocationException { InnerException: { } inner } => inner,
            _ => exception,
        };

        var (tool, host) = _resolver.ResolveAssemblyToPath(new AssemblyName(CoreName)) is { } path
            ? (ProductVersion(typeof(CmsDbContext).Assembly.Location), ProductVersion(path))
            : (typeof(CmsDbContext).Assembly.GetName().Version?.ToString(), _assembly.GetReferencedAssemblies().FirstOrDefault(reference => reference.Name == CoreName)?.Version?.ToString());

        var advice = host is not null && host != tool ? $", so use fluentcms {host}" : "";
        return $"{_assembly.GetName().Name} could not be used by this fluentcms, which has {CoreName} {tool ?? "unknown"}; the host has {host ?? "none"}{advice}. {cause.Message}";
    }

    // The package version, without the commit that the build appends.
    private static string? ProductVersion(string path) => path.Length == 0 ? null : FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0];
}
