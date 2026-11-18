using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentCMS.Admin.EndToEnd;
using Microsoft.Playwright;
using Xunit;

[assembly: AssemblyFixture(typeof(EndToEndHost))]

namespace FluentCMS.Admin.EndToEnd;

// The reference host as the image runs it, once for the run: published with no build, as the Dockerfile publishes it, and
// run as "dotnet FluentCMS.Host.dll" in a process of its own, in Production, on SQLite in a temporary directory with the
// Blog sample and the admin at /admin. It is migrated and its editor made with the host's own commands before it starts,
// and it listens on https on a free port of the loopback address with a certificate the run makes. Everything is deleted
// at the end, and the process tree is killed whatever ends the run.
public sealed class EndToEndHost : IAsyncLifetime
{
    public const string AdminPath = "/admin/";

    public const string AccessTokenLifetime = "00:00:05";

    // Under the load of the Windows full run, beside every other test app, the publish has taken from 6 to more than 10
    // minutes. It is bounded below the job's --timeout of 25 minutes, which counts from the start of this test app, with the
    // few minutes the host's start and the tests take on Windows, where the browser tests are skipped.
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(20);

    // A run left by a test process that was killed outright is swept by a later one once it has been idle this long, which no
    // run in progress is, since its host writes to its database and every run is bounded far below it.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    private const string RootPrefix = "fluentcms-e2e-";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory(RootPrefix);
    private readonly Dictionary<string, string?> _environment = [];
    private ChildProcess? _host;
    private TestCertificates? _certificates;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri Origin { get; private set; } = null!;

    public Uri Admin => new(Origin, AdminPath);

    // The editor that exists before the host starts, made as an operator makes the first user of an instance.
    public Editor Editor { get; private set; } = null!;

    // What the commands printed, for the test of the seeding.
    public string EditorCreated { get; private set; } = "";

    public string MigrateOutput { get; private set; } = "";

    // The default site of the Blog sample and a second one, made through the Management API of the multi-site plugin with a
    // key from the host's own command, since no command makes a site. The instance-wide Editor role reaches both. The media
    // plugin is disabled in the second site, where its admin half must never load.
    public IReadOnlyList<Site> Sites { get; private set; } = [];

    // Why the browser tests are skipped: Chromium is not installed for this version of Playwright.
    public string? BrowserMissing { get; private set; }

    private string PublishDirectory => Path.Combine(_root.FullName, "host");

    private string DataDirectory => Path.Combine(_root.FullName, "data");

    private string HostAssembly => Path.Combine(PublishDirectory, "FluentCMS.Host.dll");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        SweepStaleRuns();
        Directory.CreateDirectory(DataDirectory);
        await PublishAsync();
        _certificates = TestCertificates.Create(DataDirectory);
        Origin = new Uri($"https://{IPAddress.Loopback}:{FreePort()}/");
        Configure();

        MigrateOutput = await CommandAsync(null, "migrate");
        Editor = NewEditor();
        EditorCreated = await CommandAsync(Editor.Password, "user", "create", "--email", Editor.Email, "--role", "Editor", "--password-stdin");
        await StartAsync();
        Sites = await AddSecondSiteAsync();
        (_playwright, _browser, BrowserMissing) = await Chromium.LaunchAsync(ServerCertificateHash);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        }
        finally
        {
            _playwright?.Dispose();
            _host?.Dispose();
            _certificates?.Server.Dispose();
            TryDelete(_root);
        }
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file a process still holds, such as an orphaned host's, is left for a later sweep.
        }
    }

    private void SweepStaleRuns()
    {
        foreach (var run in _root.Parent!.EnumerateDirectories(RootPrefix + "*"))
        {
            if (run.FullName != _root.FullName && DateTime.UtcNow - LastWrite(run) > StaleAfter)
            {
                TryDelete(run);
            }
        }
    }

    // The latest write in the run's own folders and in its database's, which its host writes while it runs.
    private static DateTime LastWrite(DirectoryInfo run)
    {
        try
        {
            var data = new DirectoryInfo(Path.Combine(run.FullName, "data"));
            return run.EnumerateFileSystemInfos()
                .Concat(data.Exists ? data.EnumerateFileSystemInfos() : [])
                .Select(item => item.LastWriteTimeUtc)
                .Append(run.LastWriteTimeUtc)
                .Max();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.UtcNow;
        }
    }

    // A browser context of its own for a test; the test is skipped, with the reason, when Chromium is not installed. It is
    // traced when FLUENTCMS_E2E_TRACES names a directory, or when a test asks for traces itself.
    internal Task<AdminBrowser> OpenBrowserAsync(string? traces = null)
    {
        Assert.SkipWhen(BrowserMissing is not null, BrowserMissing ?? "");
        traces ??= Environment.GetEnvironmentVariable(AdminBrowser.TracesVariable) is { Length: > 0 } directory ? directory : null;
        return AdminBrowser.OpenAsync(this, _browser!, traces);
    }

    // A directory of a test's own, deleted with the host's.
    public DirectoryInfo NewDirectory() => _root.CreateSubdirectory(Guid.NewGuid().ToString("N"));

    // An editor of its own for a test that changes its user, such as by ending its sessions.
    public async Task<Editor> CreateEditorAsync()
    {
        var editor = NewEditor();
        await CommandAsync(editor.Password, "user", "create", "--email", editor.Email, "--role", "Editor", "--password-stdin");
        return editor;
    }

    // Sets a new password as an operator does with the host's command, which ends the user's sessions.
    public async Task<Editor> ResetPasswordAsync(Editor editor)
    {
        var changed = editor with { Password = NewPassword() };
        await CommandAsync(changed.Password, "user", "reset-password", "--email", editor.Email, "--password-stdin");
        return changed;
    }

    // An HTTP client that trusts the run's own certificate and no other, for the checks that need no browser.
    public HttpClient Client()
    {
        var server = _certificates!.Server;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null && certificate.RawData.AsSpan().SequenceEqual(server.RawData),
        };
        return new HttpClient(handler) { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(30) };
    }

    // The fingerprint the browser context accepts, which is this certificate's alone.
    public string ServerCertificateHash => Convert.ToBase64String(SHA256.HashData(_certificates!.Server.PublicKey.ExportSubjectPublicKeyInfo()));

    private static Editor NewEditor() => new($"editor-{Guid.NewGuid():N}@example.com", NewPassword());

    private static string NewPassword() => "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // As the Dockerfile publishes it, with the SDK alone: where the wasm-tools workload is installed, as on the Windows
    // runners, the publish would otherwise relink the WebAssembly runtime, which no image does.
    private async Task PublishAsync()
    {
        var (exitCode, _, tail) = await ChildProcess.RunAsync(
            "dotnet",
            ["publish", Repository.HostProject, "-c", Repository.Configuration, "--no-build", "-o", PublishDirectory, "-p:UseAppHost=false", "-p:WasmBuildNative=false", "-nologo", "-nodeReuse:false"],
            Repository.Root,
            new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" },
            PublishTimeout,
            input: null,
            Token);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Publishing the reference host ({Repository.Configuration}, with no build) exited with {exitCode}; build the solution in that configuration first.{Environment.NewLine}{tail}");
        }
    }

    // The settings of a production deployment, as few as the image needs, with four that only a test host has: its address
    // is a port of the loopback address; its certificates are the run's own; every browser of the tests signs in from that one
    // address, so the sign-in's limit per client address is raised from 20 a minute; and an access token lasts five seconds, so
    // that the admin refreshes it on demand, two and a half seconds after it was issued, at the next call of a test's action.
    private void Configure()
    {
        var certificates = _certificates!;
        var origin = Origin.GetLeftPart(UriPartial.Authority);
        foreach (var (name, value) in new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = null,
            ["ASPNETCORE_URLS"] = null,
            ["ASPNETCORE_HTTP_PORTS"] = null,
            ["ASPNETCORE_HTTPS_PORTS"] = null,
            ["ConnectionStrings__FluentCms"] = $"Data Source={Path.Combine(DataDirectory, "fluentcms.db")}",
            ["FLUENTCMS_SAMPLE"] = "blog",
            ["FluentCms__PublicOrigin"] = origin,
            ["AllowedHosts"] = IPAddress.Loopback.ToString(),
            ["FluentCms__Admin__Enabled"] = "true",
            ["FluentCms__Security__SigningCertificate__Path"] = certificates.SigningPath,
            ["FluentCms__Security__SigningCertificate__Password"] = certificates.Password,
            ["FluentCms__DataProtection__Certificate__Path"] = certificates.SigningPath,
            ["FluentCms__DataProtection__Certificate__Password"] = certificates.Password,
            ["FluentCms__Media__Local__Root"] = Path.Combine(DataDirectory, "media"),
            ["FluentCms__RateLimits__Auth__PermitLimit"] = "1000",
            ["FluentCms__Clients__AccessTokenLifetime"] = AccessTokenLifetime,
            ["Kestrel__Endpoints__Https__Url"] = origin,
            ["Kestrel__Endpoints__Https__Certificate__Path"] = certificates.ServerPath,
            ["Kestrel__Endpoints__Https__Certificate__Password"] = certificates.Password,
            // As the compose file sets it: a command's output is its result alone.
            ["Logging__Console__LogToStandardErrorThreshold"] = "Trace",
        })
        {
            _environment[name] = value;
        }
    }

    // secretOutput keeps what the command prints, such as a key, out of the failure message.
    private async Task<string> CommandAsync(string? input, params string[] command) => await CommandAsync(input, secretOutput: false, command);

    private async Task<string> CommandAsync(string? input, bool secretOutput, params string[] command)
    {
        var (exitCode, output, tail) = await ChildProcess.RunAsync("dotnet", ["exec", HostAssembly, "fluentcms", .. command], PublishDirectory, _environment, CommandTimeout, input, Token, secretOutput);
        return exitCode == 0
            ? output
            : throw new InvalidOperationException($"'fluentcms {string.Join(' ', command.TakeWhile(part => part != "--email"))}' exited with {exitCode}.{Environment.NewLine}{tail}");
    }

    // The key is kept in memory for these two requests only, and goes with the host's database at the end of the run.
    private async Task<IReadOnlyList<Site>> AddSecondSiteAsync()
    {
        var key = (await CommandAsync(null, secretOutput: true, "apikey", "create", "--name", "end-to-end-setup", "--class", "management", "--role", "Super Admin")).Trim();
        using var client = Client();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        using var created = await client.PostAsJsonAsync(new Uri("api/management/v1/sites", UriKind.Relative), new { name = "Second blog", slug = "second", defaultLocale = "en" }, Token);
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Creating the second site answered {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync(Token)}");
        }

        var list = await client.GetFromJsonAsync<JsonObject>(new Uri("api/management/v1/sites", UriKind.Relative), Token);
        Site[] sites = [.. list!["items"]!.AsArray().Select(site => new Site((string)site!["id"]!, (string)site["name"]!, (bool)site["isDefault"]!)).OrderByDescending(site => site.IsDefault)];
        using var disabled = await client.PutAsJsonAsync(new Uri($"api/management/v1/plugins/media/sites/{sites[1].Id}", UriKind.Relative), new { enabled = false }, Token);
        if (disabled.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Disabling media in the second site answered {(int)disabled.StatusCode}: {await disabled.Content.ReadAsStringAsync(Token)}");
        }

        return sites;
    }

    private async Task StartAsync()
    {
        _host = ChildProcess.Start("dotnet", ["exec", HostAssembly], PublishDirectory, _environment);
        using var client = Client();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bound.CancelAfter(StartTimeout);
        while (true)
        {
            if (_host.HasExited)
            {
                throw new InvalidOperationException($"The reference host exited with {_host.ExitCode} before it was ready.{Environment.NewLine}{_host.Tail}");
            }

            try
            {
                using var ready = await client.GetAsync(new Uri("health/ready", UriKind.Relative), bound.Token);
                if (ready.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (OperationCanceledException) when (bound.IsCancellationRequested && !Token.IsCancellationRequested)
            {
                throw new TimeoutException($"The reference host was not ready within {StartTimeout}.{Environment.NewLine}{_host.Tail}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), bound.Token);
            }
            catch (OperationCanceledException) when (!Token.IsCancellationRequested)
            {
                throw new TimeoutException($"The reference host was not ready within {StartTimeout}.{Environment.NewLine}{_host.Tail}");
            }
        }
    }
}

public sealed record Site(string Id, string Name, bool IsDefault);

// The password is made per run and never written out, so a test message that names the editor names its address only.
public sealed record Editor(string Email, string Password)
{
    public override string ToString() => Email;
}
