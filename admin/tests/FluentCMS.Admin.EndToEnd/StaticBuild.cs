namespace FluentCMS.Admin.EndToEnd;

// The prebuilt static build of StaticBuildTests as the Publish workflow makes a release's: FluentCMS.Admin.App published with
// a build of its own, apart from the run's, which may hold the pseudo-cultures (spec 4.17), and zipped with eng/admin-zip.cs.
// EndToEndHost starts it beside the publish of the host, so that the case waits only for what is left. No compiler server or
// build node outlives it.
internal static class StaticBuild
{
    private const string Version = "e2e";

    // The build and publish of the app beside the publish of the host, under the load of the other test apps of a full run.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    // MSBUILDDISABLENODEREUSE keeps the build nodes from staying, also for dotnet run, which hands -nodeReuse to the app.
    private static readonly string[] NoServers = ["-p:UseSharedCompilation=false", "-p:UseRazorBuildServer=false"];

    // The zip, made in the folder unless an earlier run made it there whole.
    public static async Task<string> MakeAsync(DirectoryInfo folder, CancellationToken cancellationToken)
    {
        var zip = Path.Combine(folder.FullName, $"fluentcms-admin-{Version}.zip");
        var mark = Path.Combine(folder.FullName, "made.ok");
        if (File.Exists(mark) && File.Exists(zip))
        {
            return zip;
        }

        var publish = Path.Combine(folder.FullName, "publish");
        var app = Path.Combine(Repository.Root, "admin", "src", "FluentCMS.Admin.App", "FluentCMS.Admin.App.csproj");
        string[] release = ["--artifacts-path", Path.Combine(folder.FullName, "build"), "-p:FluentCmsAdminPseudoCultures=false", "-p:WasmBuildNative=false"];
        await DotnetAsync("Publishing the admin app", ["publish", app, "-c", "Release", "-o", publish, .. release, .. NoServers, "-nologo"], cancellationToken);
        await DotnetAsync("Making the zip", ["run", Path.Combine(Repository.Root, "eng", "admin-zip.cs"), .. NoServers, "--", "pack", publish, Version, folder.FullName], cancellationToken);

        // Written last, so that a build cut short is not taken by a later run.
        await File.WriteAllTextAsync(mark, Version, cancellationToken);
        return zip;
    }

    private static async Task DotnetAsync(string step, string[] arguments, CancellationToken cancellationToken)
    {
        var (exitCode, _, tail) = await ChildProcess.RunAsync(
            "dotnet", arguments, Repository.Root, new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" }, Timeout, input: null, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{step} exited with {exitCode}.{Environment.NewLine}{tail}");
        }
    }
}
