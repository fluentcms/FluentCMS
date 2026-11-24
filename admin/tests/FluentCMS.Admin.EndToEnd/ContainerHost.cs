using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FluentCMS.Admin.EndToEnd;

// The image FLUENTCMS_E2E_IMAGE names, run through the docker command as docs/docker.md shows an operator: the host is a
// container with the settings as variables, the run's certificates and landing.schema.json mounted read-only, /data on a
// volume of the run and its https port published on the loopback address, and each command is a one-off container on the
// same volume. Values reach a container from the docker command's environment, never its arguments. The host reaches the
// run's own servers as host.docker.internal: where Docker's engine runs on this machine, as on Linux, they listen on the
// gateway of its bridge, and where it runs in a virtual machine, as with Docker Desktop, the engine forwards that name to
// the loopback address they listen on. The containers and the volume carry the run's label and are removed at its end, or
// by a later run once the run's folder is gone.
internal sealed class ContainerHost(DirectoryInfo root, string image) : HostProcess
{
    public const string Label = "org.fluentcms.e2e";

    private const string Docker = "docker";
    private const string CertificateMount = "/certificates";
    private const string SchemaMount = "/schema/landing.schema.json";
    private const string TestMachine = "host.docker.internal";

    private static readonly TimeSpan DockerTimeout = TimeSpan.FromMinutes(1);

    private readonly string _run = root.Name;
    private int _commands;
    private IPAddress _serverAddress = IPAddress.Loopback;
    private bool _removed;

    public override string CertificateDirectory => Path.Combine(root.FullName, "certificates");

    public override IPAddress ServerAddress => _serverAddress;

    public override string ServerHost => TestMachine;

    public override string SchemaPath => SchemaMount;

    private string HostContainer => $"{_run}-host";

    public override async Task<string?> PrepareAsync(CancellationToken cancellationToken)
    {
        int exitCode;
        string tail;
        try
        {
            (exitCode, _, tail) = await DockerAsync(["version", "--format", "{{.Server.Version}}"], cancellationToken);
        }
        catch (Win32Exception exception)
        {
            (exitCode, tail) = (-1, exception.Message);
        }

        if (exitCode != 0)
        {
            return $"Docker does not answer here, so the end-to-end cases on the image {image} ({ImageVariable}) are skipped; in CI a skip "
                + $"fails the job. Start Docker, or unset {ImageVariable} to run them on a publish of the host.{Environment.NewLine}{tail}";
        }

        RemoveLeftovers();
        var (inspected, _, missing) = await DockerAsync(["image", "inspect", "--format", "{{.Id}}", image], cancellationToken);
        if (inspected != 0)
        {
            throw new InvalidOperationException($"The image {image} that {ImageVariable} names is not on this machine; build it from the repository's root "
                + $"(docker build -f src/FluentCMS.Host/Dockerfile -t {image} .) or pull it.{Environment.NewLine}{missing}");
        }

        var (created, _, refused) = await DockerAsync(["volume", "create", "--label", $"{Label}={_run}", _run], cancellationToken);
        if (created != 0)
        {
            throw new InvalidOperationException($"Creating the volume {_run} exited with {created}.{Environment.NewLine}{refused}");
        }

        _serverAddress = await BridgeAddressAsync(cancellationToken);
        Directory.CreateDirectory(CertificateDirectory);
        AppDomain.CurrentDomain.ProcessExit += OnTestProcessExit;
        return null;
    }

    public override string DataPath(string name) => $"/data/{name}";

    public override string CertificatePath(string path) => $"{CertificateMount}/{Path.GetFileName(path)}";

    // Kestrel listens on every address of the container, where the published port arrives.
    public override string ListenUrl(Uri origin) => $"https://0.0.0.0:{origin.Port}";

    public override async Task<(int ExitCode, string Output, string Tail)> CommandAsync(
        IReadOnlyList<string> command, IReadOnlyDictionary<string, string?> environment, string? input, TimeSpan timeout, bool secretOutput, CancellationToken cancellationToken)
    {
        var name = $"{_run}-command-{Interlocked.Increment(ref _commands)}";
        try
        {
            return await ChildProcess.RunAsync(Docker, [.. Run(name, environment), "--interactive", image, "fluentcms", .. command], root.FullName, environment, timeout, input, cancellationToken, secretOutput);
        }
        catch
        {
            // Killing the docker command on a timeout or a cancellation leaves its container running.
            Quietly("rm", "--force", name);
            throw;
        }
    }

    public override void Start(Uri origin, IReadOnlyDictionary<string, string?> environment) =>
        Host = ChildProcess.Start(Docker, [.. Run(HostContainer, environment), "--publish", $"{IPAddress.Loopback}:{origin.Port}:{origin.Port}", image], root.FullName, environment);

    public override void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnTestProcessExit;
        Remove();
        base.Dispose();
    }

    private List<string> Run(string name, IReadOnlyDictionary<string, string?> environment)
    {
        List<string> arguments =
        [
            "run", "--rm", "--name", name, "--label", $"{Label}={_run}",
            "--add-host", $"{TestMachine}:host-gateway",
            "--mount", $"type=volume,source={_run},target=/data",
            "--mount", $"type=bind,source={CertificateDirectory},target={CertificateMount},readonly",
            "--mount", $"type=bind,source={Path.Combine(AppContext.BaseDirectory, "landing.schema.json")},target={SchemaMount},readonly",
        ];
        foreach (var (variable, value) in environment)
        {
            if (value is not null)
            {
                arguments.AddRange(["--env", variable]);
            }
        }

        return arguments;
    }

    // The gateway of Docker's default bridge when it is an address of this machine, where host-gateway leads; otherwise the
    // engine runs elsewhere and forwards host.docker.internal to the loopback address.
    private async Task<IPAddress> BridgeAddressAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output, _) = await DockerAsync(["network", "inspect", "bridge", "--format", "{{range .IPAM.Config}}{{.Gateway}} {{end}}"], cancellationToken);
        if (exitCode != 0)
        {
            return IPAddress.Loopback;
        }

        var local = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .ToHashSet();
        return output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(gateway => IPAddress.TryParse(gateway, out var address) ? address : null)
            .FirstOrDefault(address => address is { AddressFamily: AddressFamily.InterNetwork } && local.Contains(address))
            ?? IPAddress.Loopback;
    }

    private Task<(int ExitCode, string Output, string Tail)> DockerAsync(IEnumerable<string> arguments, CancellationToken cancellationToken) =>
        ChildProcess.RunAsync(Docker, arguments, root.FullName, new Dictionary<string, string?>(), DockerTimeout, input: null, cancellationToken);

    private void OnTestProcessExit(object? sender, EventArgs e) => Remove();

    private void Remove()
    {
        if (_removed)
        {
            return;
        }

        _removed = true;
        RemoveRun(_run);
    }

    // The containers and volumes of a run whose folder is gone, as one killed outright leaves them.
    private void RemoveLeftovers()
    {
        var runs = Quietly("ps", "--all", "--filter", $"label={Label}", "--format", $"{{{{.Label \"{Label}\"}}}}")
            .Concat(Quietly("volume", "ls", "--filter", $"label={Label}", "--format", $"{{{{.Label \"{Label}\"}}}}"));
        foreach (var run in runs.Distinct().Where(run => run.Length > 0 && !Directory.Exists(Path.Combine(root.Parent!.FullName, run))))
        {
            RemoveRun(run);
        }
    }

    private static void RemoveRun(string run)
    {
        var containers = Quietly("ps", "--all", "--quiet", "--filter", $"label={Label}={run}");
        if (containers.Count > 0)
        {
            Quietly(["rm", "--force", .. containers]);
        }

        Quietly("volume", "rm", "--force", run);
    }

    // A docker command whose failure changes nothing for the run, such as a removal of what is already gone; its lines of
    // output, or none.
    private static List<string> Quietly(params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Docker, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(DockerTimeout))
            {
                process.Kill(entireProcessTree: true);
                return [];
            }

            _ = error.Result;
            return process.ExitCode == 0 ? [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [];
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return [];
        }
    }
}
