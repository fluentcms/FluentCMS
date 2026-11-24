// The check of FluentCMS.Admin.Hosting as a package (admin spec 6.1, AQ8), for the Pack job: a host made of packages from a
// folder feed, in a work folder outside the repository, published and started in Production on SQLite with the admin on.
//
// - package: no app project; it serves the package's first-party build at /admin: the page with its base, config.json from
//   configuration, a fingerprinted file of the framework cached for good and in Brotli, a client route and a first-party half.
// - app <project> <half>: the host references an admin app's project and serves that app, which holds the half.
// - icons <project> <half>: as app, with a package reference of the host's own to the component library's icons, which then
//   stay in its publish.
// - client: the host references a WebAssembly project that is no admin app, which the package's build does not take for one:
//   both claim _framework/, so the build stops on conflicting assets, where taking it for an admin app would leave the host
//   with no admin to serve.
//
// In every mode but icons and client the publish root holds no icon assembly of the component library and deps.json names none. It names each
// check that fails and exits with 1. The work folder is outside the repository, whose Directory.Packages.props would pin the
// packages. Packages restore into admin-host-packages beside it, shared by the runs, with every FluentCMS package removed first,
// so that one is never taken from an earlier pack of the same version.
//
//   dotnet run eng/admin-package-host.cs -- <folder feed> <version> <work folder> [package | app|icons <project> <half> | client]

// A file-based app publishes with Native AOT by default, which would restore the ILCompiler package for nothing.
#:property PublishAot=false

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

const string Icons = "Microsoft.FluentUI.AspNetCore.Components.Icons";
const string FirstPartyHalf = "FluentCMS.Plugins.Media.Admin";
const string Branding = "Packaged admin";

var mode = args.Length > 3 ? args[3] : "package";
if (!((args.Length == 3 || (args.Length == 4 && mode is "package" or "client") || (args.Length == 6 && mode is "app" or "icons")) && Directory.Exists(args[0])))
{
    Console.Error.WriteLine("usage: dotnet run eng/admin-package-host.cs -- <folder feed> <version> <work folder> [package | app|icons <project> <half> | client]");
    return 2;
}

var feed = Path.GetFullPath(args[0]);
var version = args[1];
var work = Path.GetFullPath(args[2]);
var app = args.Length == 6 ? Path.GetFullPath(args[4]) : null;
var half = args.Length == 6 ? args[5] : null;
var host = Path.Combine(work, "host");
var publish = Path.Combine(work, "publish");
var packages = Path.Combine(Path.GetDirectoryName(work)!, "admin-host-packages");
if (Directory.Exists(work))
{
    Directory.Delete(work, recursive: true);
}

foreach (var cached in Directory.Exists(packages) ? Directory.GetDirectories(packages, "fluentcms.*") : [])
{
    Directory.Delete(cached, recursive: true);
}

Directory.CreateDirectory(host);
File.WriteAllText(Path.Combine(host, "nuget.config"), $"""
    <?xml version="1.0" encoding="utf-8"?>
    <configuration>
      <packageSources>
        <clear />
        <add key="feed" value="{feed}" />
        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
      </packageSources>
    </configuration>
    """);

var references = new List<string>();
if (app is not null)
{
    references.Add($"<ProjectReference Include=\"{app}\" />");
}

if (mode == "icons")
{
    references.Add($"<PackageReference Include=\"{Icons}\" Version=\"{Pinned(Icons)}\" />");
}

if (mode == "client")
{
    var client = Path.Combine(work, "client");
    Directory.CreateDirectory(client);
    File.WriteAllText(Path.Combine(client, "Client.csproj"), $$"""
        <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="{{Pinned("Microsoft.AspNetCore.Components.WebAssembly")}}" />
          </ItemGroup>
        </Project>
        """);
    File.WriteAllText(Path.Combine(client, "Program.cs"), "await Microsoft.AspNetCore.Components.WebAssembly.Hosting.WebAssemblyHostBuilder.CreateDefault(args).Build().RunAsync();\n");
    File.Copy(Path.Combine(host, "nuget.config"), Path.Combine(client, "nuget.config"));
    references.Add($"<ProjectReference Include=\"{Path.Combine(client, "Client.csproj")}\" />");
}

File.WriteAllText(Path.Combine(host, "Host.csproj"), $"""
    <Project Sdk="Microsoft.NET.Sdk.Web">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="FluentCMS.Admin.Hosting" Version="{version}" />
        <PackageReference Include="FluentCMS.Data.Sqlite" Version="{version}" />
        {string.Join("\n    ", references)}
      </ItemGroup>
    </Project>
    """);
File.WriteAllText(Path.Combine(host, "Program.cs"), """
    using FluentCMS.Admin.Hosting;
    using FluentCMS.Core.DependencyInjection;
    using FluentCMS.Data.Sqlite;

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("cms")).AddAdmin());

    var app = builder.Build();
    app.UseFluentCms();
    app.MapFluentCms();
    app.MapFluentCmsAdmin();
    app.Run();
    """);

var problems = new List<string>();
var what = mode switch
{
    "app" => $"the app {Path.GetFileNameWithoutExtension(app)}",
    "icons" => $"the app {Path.GetFileNameWithoutExtension(app)} beside a reference of the host's own to the icons",
    "client" => "the package's build beside a WebAssembly project that is no admin app",
    _ => "the package's build",
};

var (exitCode, output) = Run("dotnet", "publish", host, "-c", "Release", "-o", publish, "-nologo", $"-p:RestorePackagesPath={packages}");
if (mode == "client")
{
    Expect(exitCode != 0 && output.Contains("Conflicting assets with the same target path '_framework/", StringComparison.Ordinal),
        "the publish stops on the conflicting _framework/ files and does not take the project for an admin app");
    if (problems.Count > 0)
    {
        Console.WriteLine(output);
    }

    return Report(problems);
}

if (exitCode != 0)
{
    Console.WriteLine(output);
    return Report([$"The host of {what} did not publish."]);
}

var iconFiles = Directory.EnumerateFiles(publish).Select(Path.GetFileName).Where(name => name!.StartsWith(Icons, StringComparison.Ordinal)).ToList();
var namesIcons = File.ReadAllText(Path.Combine(publish, "Host.deps.json")).Contains(Icons, StringComparison.Ordinal);
if (mode == "icons")
{
    Expect(iconFiles.Count > 0 && namesIcons, "the publish root and deps.json keep the icon assemblies the host references itself");
}
else
{
    Expect(iconFiles.Count == 0, $"the publish root holds no {Icons} assembly");
    Expect(!namesIcons, $"deps.json names no {Icons}");
}

var fingerprinted = FingerprintedFile(Path.Combine(publish, "Host.staticwebassets.endpoints.json"));
if (fingerprinted is null)
{
    problems.Add("The host's endpoints name no fingerprinted file of the framework cached for good with a Brotli copy.");
}

var port = FreePort();
var start = new ProcessStartInfo("dotnet", [Path.Combine(publish, "Host.dll")]) { WorkingDirectory = publish, RedirectStandardOutput = true, RedirectStandardError = true };
start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
start.Environment["ConnectionStrings__cms"] = $"Data Source={Path.Combine(work, "cms.db")}";
start.Environment["FluentCms__PublicOrigin"] = "https://localhost";
start.Environment["FluentCms__Security__AllowGeneratedSigningKey"] = "true";
start.Environment["FluentCms__Security__AllowUnencryptedKeyRing"] = "true";
start.Environment["FluentCms__Admin__Enabled"] = "true";
start.Environment["FluentCms__Admin__Branding__Name"] = Branding;

using var process = Process.Start(start)!;
var log = new System.Text.StringBuilder();
process.OutputDataReceived += (_, line) => log.AppendLine(line.Data);
process.ErrorDataReceived += (_, line) => log.AppendLine(line.Data);
process.BeginOutputReadLine();
process.BeginErrorReadLine();

using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = false })
{
    BaseAddress = new Uri($"http://127.0.0.1:{port}"),
};

try
{
    if (!await ReadyAsync())
    {
        Console.WriteLine(log);
        return Report([.. problems, $"The host of {what} did not start."]);
    }

    using var page = await http.GetAsync("/admin/");
    var html = await page.Content.ReadAsStringAsync();
    Expect(page.StatusCode == HttpStatusCode.OK && html.Contains("<base href=\"/admin/\" />", StringComparison.Ordinal), "/admin/ answers the page with its base set");
    Expect(page.Headers.CacheControl?.NoStore == true, "/admin/ is never cached");

    using var route = await http.GetAsync("/admin/content/article");
    Expect(route.StatusCode == HttpStatusCode.OK && await route.Content.ReadAsStringAsync() == html, "a client route answers the page");

    using var configuration = await http.GetAsync("/admin/config.json");
    var json = configuration.StatusCode == HttpStatusCode.OK ? JsonDocument.Parse(await configuration.Content.ReadAsStringAsync()).RootElement : default;
    Expect(json.ValueKind == JsonValueKind.Object && json.GetProperty("branding").GetProperty("name").GetString() == Branding, "/admin/config.json is built from the host's configuration");

    if (fingerprinted is not null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/" + fingerprinted);
        request.Headers.AcceptEncoding.ParseAdd("br");
        using var file = await http.SendAsync(request);
        Expect(file.StatusCode == HttpStatusCode.OK && file.Content.Headers.ContentEncoding.Contains("br"), $"/admin/{fingerprinted} answers its Brotli copy");
        Expect(file.Headers.CacheControl?.ToString().Contains("immutable", StringComparison.Ordinal) == true, $"/admin/{fingerprinted} is cached for good");
    }

    using var plugins = await http.GetAsync("/admin/fluentcms-admin.plugins.json");
    var halves = await plugins.Content.ReadAsStringAsync();
    Expect(plugins.StatusCode == HttpStatusCode.OK, "/admin/fluentcms-admin.plugins.json answers the gathered manifests");
    if (half is null)
    {
        Expect(halves.Contains(FirstPartyHalf, StringComparison.Ordinal), $"the package's build holds the first-party half {FirstPartyHalf}");
    }
    else
    {
        Expect(halves.Contains(half, StringComparison.Ordinal), $"the host serves its own app, which holds {half}");
    }
}
finally
{
    process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
}

if (problems.Count > 0)
{
    Console.WriteLine(log);
}

return Report(problems);

void Expect(bool holds, string check)
{
    if (holds)
    {
        Console.WriteLine($"ok    {what}: {check}");
    }
    else
    {
        problems.Add($"Not so for {what}: {check}.");
    }
}

int Report(List<string> found)
{
    foreach (var problem in found)
    {
        Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::error::{problem}" : $"FAIL  {problem}");
    }

    return found.Count == 0 ? 0 : 1;
}

async Task<bool> ReadyAsync()
{
    for (var attempt = 0; attempt < 120 && !process.HasExited; attempt++)
    {
        try
        {
            using var live = await http.GetAsync("/health/live");
            if (live.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch (HttpRequestException)
        {
        }

        await Task.Delay(500);
    }

    return false;
}

// Runs a command and returns its exit code and what it wrote, which is printed only when a check needs it.
static (int ExitCode, string Output) Run(string file, params string[] arguments)
{
    using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
    var error = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output + error.Result);
}

// The version of a package that the repository the check runs in pins, as the admin's own projects use it.
static string Pinned(string id) =>
    Regex.Match(File.ReadAllText("Directory.Packages.props"), $"""<PackageVersion Include="{Regex.Escape(id)}" Version="(?<version>[^"]+)" />""").Groups["version"].Value;

static int FreePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

// A route of the publish's endpoints under _framework/ with a fingerprint, cached for good, that has a Brotli copy.
static string? FingerprintedFile(string manifest)
{
    using var document = JsonDocument.Parse(File.ReadAllText(manifest));
    return document.RootElement.GetProperty("Endpoints").EnumerateArray()
        .Where(endpoint => endpoint.GetProperty("Route").GetString()!.StartsWith("_framework/", StringComparison.Ordinal)
            && endpoint.GetProperty("Route").GetString()!.EndsWith(".wasm", StringComparison.Ordinal)
            && endpoint.GetProperty("EndpointProperties").EnumerateArray().Any(property => property.GetProperty("Name").GetString() == "fingerprint")
            && endpoint.GetProperty("Selectors").EnumerateArray().Any(selector => selector.GetProperty("Value").GetString() == "br")
            && endpoint.GetProperty("ResponseHeaders").EnumerateArray().Any(header => header.GetProperty("Value").GetString()!.Contains("immutable", StringComparison.Ordinal)))
        .Select(endpoint => endpoint.GetProperty("Route").GetString())
        .FirstOrDefault();
}
