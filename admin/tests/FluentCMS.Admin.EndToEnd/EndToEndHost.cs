using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FluentCMS.Admin.EndToEnd;
using Xunit;

[assembly: AssemblyFixture(typeof(EndToEndHost))]

// One host serves every test, and the browser tests drive one tab or two at a time, so they run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FluentCMS.Admin.EndToEnd;

// The reference host as the image runs it, once for the run: published with no build, as the Dockerfile publishes it, and
// run as "dotnet FluentCMS.Host.dll" in a process of its own, in Production, on SQLite in a temporary directory with the
// Blog sample and the admin at /admin. It is migrated and its editor made with the host's own commands before it starts,
// and it listens on https on a free port of the loopback address with a certificate the run makes. Everything is deleted
// at the end, and the process tree is killed whatever ends the run.
public sealed class EndToEndHost : IAsyncLifetime
{
    public const string AdminPath = "/admin/";

    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("fluentcms-e2e-");
    private readonly Dictionary<string, string?> _environment = [];
    private ChildProcess? _host;
    private TestCertificates? _certificates;

    public Uri Origin { get; private set; } = null!;

    public Uri Admin => new(Origin, AdminPath);

    // The editor that exists before the host starts, made as an operator makes the first user of an instance.
    public Editor Editor { get; private set; } = null!;

    // What the commands printed, for the test of the seeding.
    public string EditorCreated { get; private set; } = "";

    public string MigrateOutput { get; private set; } = "";

    private string PublishDirectory => Path.Combine(_root.FullName, "host");

    private string DataDirectory => Path.Combine(_root.FullName, "data");

    private string HostAssembly => Path.Combine(PublishDirectory, "FluentCMS.Host.dll");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        await PublishAsync();
        _certificates = TestCertificates.Create(DataDirectory);
        Origin = new Uri($"https://{IPAddress.Loopback}:{FreePort()}/");
        Configure();

        MigrateOutput = await CommandAsync(null, "migrate");
        Editor = NewEditor();
        EditorCreated = await CommandAsync(Editor.Password, "user", "create", "--email", Editor.Email, "--role", "Editor", "--password-stdin");
        await StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _host?.Dispose();
        _certificates?.Server.Dispose();
        await Task.Yield();
        try
        {
            _root.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

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

    private async Task PublishAsync()
    {
        var (exitCode, _, tail) = await ChildProcess.RunAsync(
            "dotnet",
            ["publish", Repository.HostProject, "-c", Repository.Configuration, "--no-build", "-o", PublishDirectory, "-p:UseAppHost=false", "-nologo", "-nodeReuse:false"],
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

    // The settings of a production deployment, as few as the image needs, with three that only a test host has: its address
    // is a port of the loopback address; its certificates are the run's own; and every browser of the tests signs in from
    // that one address, so the sign-in's limit per client address is raised from 20 a minute.
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

    private async Task<string> CommandAsync(string? input, params string[] command)
    {
        var (exitCode, output, tail) = await ChildProcess.RunAsync("dotnet", ["exec", HostAssembly, "fluentcms", .. command], PublishDirectory, _environment, CommandTimeout, input, Token);
        return exitCode == 0
            ? output
            : throw new InvalidOperationException($"'fluentcms {string.Join(' ', command.TakeWhile(part => part != "--email"))}' exited with {exitCode}.{Environment.NewLine}{tail}");
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

// The password is made per run and never written out, so a test message that names the editor names its address only.
public sealed record Editor(string Email, string Password)
{
    public override string ToString() => Email;
}
