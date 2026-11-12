using System.Text.Json;

namespace FluentCMS.Cli;

// The host a project command runs: its built assembly, and the project directory, which is the host's content root.
internal sealed record HostTarget(string AssemblyPath, string ProjectDirectory, string RootNamespace)
{
    public string Name => Path.GetFileNameWithoutExtension(AssemblyPath);

    // --assembly names a built host and builds nothing; otherwise the project is built unless --no-build says it is.
    public static HostTarget Resolve(string? project, string? assembly, bool noBuild, TextWriter output)
    {
        if (assembly is not null)
        {
            var path = Path.GetFullPath(assembly);
            return File.Exists(path)
                ? new HostTarget(path, Path.GetFullPath(project ?? "."), Path.GetFileNameWithoutExtension(path))
                : throw new CliException(CliErrorCodes.AssemblyNotFound, $"{path} does not exist.");
        }

        var target = project ?? ".";
        if (!noBuild)
        {
            output.WriteLine($"Building {target}");
            Dotnet.Run(CliErrorCodes.ProjectNotBuilt, "build", target);
        }

        using var json = JsonDocument.Parse(Dotnet.Run(CliErrorCodes.ProjectNotBuilt, "msbuild", target, "-getProperty:TargetPath", "-getProperty:RootNamespace", "-getProperty:MSBuildProjectDirectory"));
        var properties = json.RootElement.GetProperty("Properties");
        var host = new HostTarget(
            properties.GetProperty("TargetPath").GetString()!,
            properties.GetProperty("MSBuildProjectDirectory").GetString()!,
            properties.GetProperty(nameof(RootNamespace)).GetString()!);

        return File.Exists(host.AssemblyPath)
            ? host
            : throw new CliException(CliErrorCodes.AssemblyNotFound, $"{host.AssemblyPath} does not exist; build {target}, or leave out --no-build.");
    }
}
