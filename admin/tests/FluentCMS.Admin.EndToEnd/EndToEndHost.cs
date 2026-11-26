using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Admin.EndToEnd;
using Microsoft.Playwright;
using Xunit;

[assembly: AssemblyFixture(typeof(EndToEndHost))]

namespace FluentCMS.Admin.EndToEnd;

// The reference host as the image runs it, once for the run: a publish of it run with dotnet, or a container of the image
// FLUENTCMS_E2E_IMAGE names (HostProcess), in Production, on SQLite with the Blog sample, the type of landing.schema.json,
// and the admin at /admin. It is migrated and its editor made with the host's own commands before it starts, and it
// listens on https on a free port of the loopback address with a certificate the run makes. Everything is deleted at the
// end, and the host is stopped whatever ends the run.
public sealed partial class EndToEndHost : IAsyncLifetime
{
    public const string AdminPath = "/admin/";

    public const string AccessTokenLifetime = "00:00:05";

    // The front end of PreviewTests, which no server holds: the case's browser answers its requests itself.
    public const string PreviewOrigin = "https://preview.test";

    // Where StaticBuildTests serves the static build, on an origin of its own.
    public const string StaticPath = "/team/admin/";

    // A run left by a test process that was killed outright is swept by a later one once it has been idle this long, which no
    // run in progress is, since its host writes to its database and every run is bounded far below it.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    private const string RootPrefix = "fluentcms-e2e-";

    // Requests a minute, per address and per user, far above what the serial suite sends.
    private const string TestLimit = "100000";
    private const string ReaderRole = "Reader";
    private const string EditorRoleId = "00000000-0000-7000-8000-000000000002";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory(RootPrefix);
    private readonly Dictionary<string, string?> _environment = [];
    private readonly ConcurrentDictionary<Type, Lazy<Task<Editor>>> _classEditors = new();
    private HostProcess? _host;
    private SmtpSink? _smtp;
    private AiProviderStub? _ai;
    private TestCertificates? _certificates;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _staticBuildGate = new();
    private Task<string>? _staticBuild;

    public Uri Origin { get; private set; } = null!;

    public Uri Admin => new(Origin, AdminPath);

    // The origin of the static build, another site than the host's, whose port is taken when the case serves the build.
    public Uri StaticOrigin { get; private set; } = null!;

    public Uri StaticAdmin => new(StaticOrigin, StaticPath);

    // The editor that exists before the host starts, made as an operator makes the first user of an instance.
    public Editor Editor { get; private set; } = null!;

    // What the commands printed, for the test of the seeding.
    public string EditorCreated { get; private set; } = "";

    public string MigrateOutput { get; private set; } = "";

    // The default site of the Blog sample and a second one, made through the Management API of the multi-site plugin with a
    // key from the host's own command, since no command makes a site. The instance-wide Editor role reaches both. The media
    // plugin is disabled in the second site, where its admin half must never load. The same key makes the role Reader.
    public IReadOnlyList<Site> Sites { get; private set; } = [];

    // A user of the instance-wide role Reader, which holds the Editor's grants without those of media, so that the media
    // plugin's contributions are hidden from it in a site where the plugin is enabled.
    public Editor Reader { get; private set; } = null!;

    // The AI provider the sites of the run may name, which the host allows as an endpoint.
    internal AiProviderStub Ai => _ai!;

    // The address the run's own servers listen on, and the name the host reaches it by.
    internal IPAddress ServerAddress => _host!.ServerAddress;

    internal string ServerHost => _host!.ServerHost;

    // The last lines the host logged, for the output of a failed case, with the query of each address cut: a request line
    // holds it, and it can hold what the browser never saw, such as an authorization code, which no mask then knows.
    internal string HostLog => AddressQuery().Replace(_host?.Tail ?? "", "$1?[query]");

    // Why the browser tests are skipped: the run's browser is not installed for this version of Playwright.
    public string? BrowserMissing { get; private set; }

    // The browser of the run, which FLUENTCMS_E2E_BROWSER names; Chromium when it is unset.
    internal BrowserEngine Engine { get; } = BrowserEngine.Chosen();

    // Why every case is skipped: the host cannot run here, as when the image is asked for and Docker does not answer.
    public string? HostMissing { get; private set; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        SlowMode.Refuse(Engine);
        SweepStaleRuns();
        _host = HostProcess.Create(_root);

        // Beside the publish of the host, where the case of the static build can run; it asks for it itself otherwise.
        if (_host is PublishedHost && Engine.LooksInstalled())
        {
            _ = StaticBuildAsync();
        }

        HostMissing = await _host.PrepareAsync(Token);
        if (HostMissing is not null)
        {
            return;
        }

        _smtp = new SmtpSink(_host.ServerAddress);
        _ai = new AiProviderStub(_host.ServerAddress, _host.ServerHost);
        _certificates = TestCertificates.Create(_host.CertificateDirectory);
        Origin = new Uri($"https://{IPAddress.Loopback}:{FreePort()}/");
        StaticOrigin = new Uri($"https://localhost:{FreePort()}/");
        Configure();

        MigrateOutput = await CommandAsync(null, "migrate");
        Editor = NewEditor();
        EditorCreated = await CommandAsync(Editor.Password, "user", "create", "--email", Editor.Email, "--role", "Editor", "--password-stdin");
        await StartAsync();
        Sites = await AddSecondSiteAsync();
        Reader = NewEditor();
        await CommandAsync(Reader.Password, "user", "create", "--email", Reader.Email, "--role", ReaderRole, "--password-stdin");
        (_playwright, _browser, BrowserMissing) = await Engine.LaunchAsync(ServerCertificateHash);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // A static build no case waited for is stopped, and its failure is no failure of the run.
            await _stopping.CancelAsync();
            if (_staticBuild is { } build)
            {
                await ((Task)build).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        }
        finally
        {
            _playwright?.Dispose();
            _host?.Dispose();
            _smtp?.Dispose();
            _ai?.Dispose();
            _certificates?.Server.Dispose();
            _stopping.Dispose();
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

    // The host for a test class, which skips its cases, with the reason, when the host cannot run here.
    public EndToEndHost Running()
    {
        Assert.SkipWhen(HostMissing is not null, HostMissing ?? "");
        return this;
    }

    // A browser context of its own for a test; the test is skipped, with the reason, when the browser is not installed. It is
    // traced when FLUENTCMS_E2E_TRACES names a directory, or when a test asks for traces itself.
    internal Task<AdminBrowser> OpenBrowserAsync(string? traces = null)
    {
        Assert.SkipWhen(BrowserMissing is not null, BrowserMissing ?? "");
        traces ??= Environment.GetEnvironmentVariable(AdminBrowser.TracesVariable) is { Length: > 0 } directory ? directory : null;
        return AdminBrowser.OpenAsync(this, _browser!, traces);
    }

    // The zip of the static build (StaticBuild), made once for the run, and once for the runs that FLUENTCMS_E2E_PUBLISHED
    // keeps the host's publish for, beside it, as the retry of the CI job's failed cases does.
    internal Task<string> StaticBuildAsync()
    {
        lock (_staticBuildGate)
        {
            return _staticBuild ??= StaticBuild.MakeAsync(
                Environment.GetEnvironmentVariable(PublishedHost.PublishedVariable) is { Length: > 0 } kept
                    ? Directory.CreateDirectory(Path.Combine(kept, "static-build"))
                    : _root.CreateSubdirectory("static-build"),
                _stopping.Token);
        }
    }

    // A directory of a test's own, deleted with the host's.
    public DirectoryInfo NewDirectory() => _root.CreateSubdirectory(Guid.NewGuid().ToString("N"));

    // An editor of its own for a test that changes its user, such as by ending its sessions, or a user of another role: an
    // instance-wide one, or with the slug of a site, a role of that site.
    public async Task<Editor> CreateEditorAsync(string role = "Editor", string? site = null)
    {
        var editor = NewEditor();
        string[] ofSite = site is null ? [] : ["--site", site];
        await CommandAsync(editor.Password, ["user", "create", "--email", editor.Email, "--role", role, .. ofSite, "--password-stdin"]);
        return editor;
    }

    // The editor of one test class, made once for it.
    internal Task<Editor> EditorOfAsync(Type testClass) =>
        _classEditors.GetOrAdd(testClass, _ => new Lazy<Task<Editor>>(() => CreateEditorAsync())).Value;

    // Sets a new password as an operator does with the host's command, which ends the user's sessions. The command also has
    // the user choose a new password at the next sign-in; the operator here takes that back, so that the next sign-in goes
    // back to where it started, as the cases that end a session check.
    public async Task<Editor> ResetPasswordAsync(Editor editor)
    {
        var changed = editor with { Password = NewPassword() };
        await CommandAsync(changed.Password, "user", "reset-password", "--email", editor.Email, "--password-stdin");

        await SetMustChangePasswordAsync(editor, false);
        return changed;
    }

    // Has the user choose a new password at its next sign-in, as an administrator does on the user's page.
    public Task MustChangePasswordAsync(Editor editor) => SetMustChangePasswordAsync(editor, true);

    // A role of the case's own, named by its mark, with the grants of the built-in Editor, which requires two-factor sign-in.
    // Only the users the case makes with it hold it, so no other case signs in under it.
    public async Task<string> CreateTwoFactorRoleAsync(string mark)
    {
        var name = $"Two-factor editor {mark}";
        using var client = await ManagementClientAsync();
        var editor = await client.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/roles/{EditorRoleId}", UriKind.Relative), Token);
        using var created = await client.PostAsJsonAsync(
            new Uri("api/management/v1/roles", UriKind.Relative),
            new JsonObject { ["name"] = name, ["requireTwoFactor"] = true, ["grants"] = editor!["grants"]!.DeepClone() },
            Token);
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Creating the role {name} answered {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync(Token)}");
        }

        return name;
    }

    private async Task SetMustChangePasswordAsync(Editor editor, bool mustChange)
    {
        using var client = await ManagementClientAsync();
        var found = await client.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/users?search={Uri.EscapeDataString(editor.Email)}", UriKind.Relative), Token);
        var id = found!["items"]!.AsArray().Single(user => (string?)user!["email"] == editor.Email)!["id"]!.GetValue<string>();
        using var kept = await client.PatchAsJsonAsync(new Uri($"api/management/v1/users/{id}", UriKind.Relative), new { mustChangePassword = mustChange }, Token);
        kept.EnsureSuccessStatusCode();
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

    // A client of the Management API with a management key of its own, for a case that makes its data before the browser
    // acts on it. The key goes with the host's database at the end of the run.
    public async Task<HttpClient> ManagementClientAsync()
    {
        var key = (await CommandAsync(null, secretOutput: true, "apikey", "create", "--name", $"end-to-end-{Guid.NewGuid():N}", "--class", "management", "--role", "Super Admin")).Trim();
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    // The fingerprint the browser context accepts, which is this certificate's alone.
    public string ServerCertificateHash => Convert.ToBase64String(SHA256.HashData(_certificates!.Server.PublicKey.ExportSubjectPublicKeyInfo()));

    private static Editor NewEditor() => new($"editor-{Guid.NewGuid():N}@example.com", NewPassword());

    internal static string NewPassword() => "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // The culture of the run, FluentCms:Admin:Cultures:Default: FLUENTCMS_E2E_CULTURE, else ar-XB when the host is published
    // with the pseudo-cultures (PublishedHost), and none, so English, on the image or a publish without them (spec 4.17). The
    // browsers of the harness are in en-US, which the admin offers, so a case gets it only in a browser whose language the
    // admin does not offer.
    public const string CultureVariable = "FLUENTCMS_E2E_CULTURE";

    public static string? Culture { get; } =
        Environment.GetEnvironmentVariable(CultureVariable) is { Length: > 0 } culture ? culture : Repository.PseudoCultures ? "ar-XB" : null;

    // The settings of a production deployment, as few as the image needs, with six that only a test host has: its address
    // is a port of the loopback address; its certificates are the run's own; every browser of the tests calls from that one
    // address, one case after another for minutes, so the request limits per address and per user are raised to what such a
    // suite cannot reach (no case is about the limiter, whose tests are the host's own); and an access token lasts five
    // seconds, so that the admin refreshes it on demand, two and a half seconds after it was issued, at the next call of a
    // test's action; the preview origin of PreviewTests, which no server holds; a mail server of the run's own (SmtpSink),
    // which takes the invitations the users page sends and keeps none; an AI provider of the run's own (AiProviderStub),
    // the one endpoint a site may name in its AI settings; and webhooks that may deliver to private networks, so that the
    // receiver of WebhooksTests is reached. The admin's client is configured whole, as for an admin on another origin (spec
    // 6.4): what the hosting package would register (AdminClientRegistration: its display name, a public client of the admin
    // pool with no consent, the host's own addresses and the five scopes), and the addresses of the static build of
    // StaticBuildTests, whose origin the Management API's CORS lists. The paths and the address Kestrel listens on are where the host finds them, and
    // the run's own servers are reached by the name the host knows this machine by (HostProcess).
    private void Configure()
    {
        var certificates = _certificates!;
        var origin = Origin.GetLeftPart(UriPartial.Authority);
        const string Client = "FluentCms__Clients__Applications__fluentcms-admin__";
        string[] scopes = ["openid", "profile", "email", "offline_access", "management"];
        foreach (var (name, value) in new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = null,
            ["ASPNETCORE_URLS"] = null,
            ["ASPNETCORE_HTTP_PORTS"] = null,
            ["ASPNETCORE_HTTPS_PORTS"] = null,
            ["ConnectionStrings__FluentCms"] = $"Data Source={_host!.DataPath("fluentcms.db")}",
            ["FLUENTCMS_SAMPLE"] = "blog",
            ["FLUENTCMS_SCHEMA"] = _host.SchemaPath,
            ["FluentCms__PublicOrigin"] = origin,
            ["AllowedHosts"] = IPAddress.Loopback.ToString(),
            ["FluentCms__Admin__PreviewOrigins__0"] = PreviewOrigin,
            ["FluentCms__Admin__Cultures__Default"] = Culture,
            ["FluentCms__Security__SigningCertificate__Path"] = _host.CertificatePath(certificates.SigningPath),
            ["FluentCms__Security__SigningCertificate__Password"] = certificates.Password,
            ["FluentCms__DataProtection__Certificate__Path"] = _host.CertificatePath(certificates.SigningPath),
            ["FluentCms__DataProtection__Certificate__Password"] = certificates.Password,
            ["FluentCms__Media__Local__Root"] = _host.DataPath("media"),
            ["FluentCms__RateLimits__Auth__PermitLimit"] = TestLimit,
            ["FluentCms__RateLimits__Management__PermitLimit"] = TestLimit,
            ["FluentCms__RateLimits__Delivery__PermitLimit"] = TestLimit,
            ["FluentCms__RateLimits__Delivery__MaxKeyLimit"] = TestLimit,
            ["FluentCms__RateLimits__PreAuth__PermitLimit"] = TestLimit,
            ["FluentCms__Clients__AccessTokenLifetime"] = AccessTokenLifetime,
            ["FluentCms__Email__Smtp__Host"] = _host.ServerHost,
            ["FluentCms__Email__Smtp__Port"] = _smtp!.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FluentCms__Email__Smtp__Security"] = "None",
            ["FluentCms__Email__Smtp__From"] = "cms@example.com",
            ["FluentCms__Ai__AllowedEndpoints__0"] = _ai!.Endpoint,
            ["FluentCms__Webhooks__AllowPrivateNetworks"] = "true",
            ["FluentCms__Security__Cors__Management__AllowedOrigins__0"] = StaticOrigin.GetLeftPart(UriPartial.Authority),
            [Client + "DisplayName"] = "FluentCMS admin",
            [Client + "RedirectUris__0"] = new Uri(Admin, "authentication/login-callback").AbsoluteUri,
            [Client + "RedirectUris__1"] = new Uri(StaticAdmin, "authentication/login-callback").AbsoluteUri,
            [Client + "PostLogoutRedirectUris__0"] = Admin.AbsoluteUri,
            [Client + "PostLogoutRedirectUris__1"] = StaticAdmin.AbsoluteUri,
            ["Kestrel__Endpoints__Https__Url"] = _host.ListenUrl(Origin),
            ["Kestrel__Endpoints__Https__Certificate__Path"] = _host.CertificatePath(certificates.ServerPath),
            ["Kestrel__Endpoints__Https__Certificate__Password"] = certificates.Password,
            // As the compose file sets it: a command's output is its result alone.
            ["Logging__Console__LogToStandardErrorThreshold"] = "Trace",
            // For the host's log in the output of a failed case (HostLog): a line, with its time, for the start and the end of
            // each request, and none for the commands the background jobs send the database every few seconds, nor for the
            // endpoint and the file of each request, which say again what its start says.
            ["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware"] = "Warning",
            ["Logging__LogLevel__Microsoft.AspNetCore.StaticAssets"] = "Warning",
            ["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
            // A line for each connection's start and end and for a connection or a stream the server closed or the client reset,
            // with the reason, so that an answer the browser lost shows whether its connection went (#3635). Debug leaves out
            // the frames of HTTP/2, which are Trace.
            ["Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel.Connections"] = "Debug",
            ["Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel.Http2"] = "Debug",
            ["Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel.BadRequests"] = "Debug",
            // Named, or the console logger reads the formatter's options from elsewhere.
            ["Logging__Console__FormatterName"] = "simple",
            ["Logging__Console__FormatterOptions__SingleLine"] = "true",
            ["Logging__Console__FormatterOptions__TimestampFormat"] = "HH:mm:ss.fff ",
            ["Logging__Console__FormatterOptions__UseUtcTimestamp"] = "true",
        })
        {
            _environment[name] = value;
        }

        for (var index = 0; index < scopes.Length; index++)
        {
            _environment[$"{Client}Scopes__{index}"] = scopes[index];
        }
    }

    // An absolute address, or a path at the start of a word, such as that of a redirect.
    [GeneratedRegex(@"((?:\b[a-z][a-z0-9+.-]*://|(?<![^\s'""(=])/)[^\s?'""]*)\?[^\s'""]*")]
    private static partial Regex AddressQuery();

    // The certificate of the host with its key, for a server of a case on another origin, which the browser accepts by the same key.
    internal X509Certificate2 LoadServerCertificate() => X509CertificateLoader.LoadPkcs12FromFile(_certificates!.ServerPath, _certificates.Password);

    // secretOutput keeps what the command prints, such as a key, out of the failure message.
    private async Task<string> CommandAsync(string? input, params string[] command) => await CommandAsync(input, secretOutput: false, command);

    private async Task<string> CommandAsync(string? input, bool secretOutput, params string[] command)
    {
        var (exitCode, output, tail) = await _host!.CommandAsync(command, _environment, input, CommandTimeout, secretOutput, Token);
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

        var editor = await client.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/roles/{EditorRoleId}", UriKind.Relative), Token);
        var grants = new JsonArray([.. editor!["grants"]!.AsArray().Where(grant => !((string)grant!["permission"]!).StartsWith("media.", StringComparison.Ordinal)).Select(grant => grant!.DeepClone())]);
        using var reader = await client.PostAsJsonAsync(new Uri("api/management/v1/roles", UriKind.Relative), new JsonObject { ["name"] = ReaderRole, ["grants"] = grants }, Token);
        if (reader.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Creating the role {ReaderRole} answered {(int)reader.StatusCode}: {await reader.Content.ReadAsStringAsync(Token)}");
        }

        return sites;
    }

    private async Task StartAsync()
    {
        _host!.Start(Origin, _environment);
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
