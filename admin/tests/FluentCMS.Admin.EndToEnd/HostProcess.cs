using System.Net;

namespace FluentCMS.Admin.EndToEnd;

// The reference host of a run, in one of two ways: a publish of it run with dotnet, as a developer runs the tests, or, when
// FLUENTCMS_E2E_IMAGE names an image, a container of that image, as the job on the image runs them (spec 7). Either way it
// runs the host's commands, starts the host once, gives its logs and stops it; the settings name the files and addresses
// the host finds where it runs.
internal abstract class HostProcess : IDisposable
{
    public const string ImageVariable = "FLUENTCMS_E2E_IMAGE";

    protected ChildProcess? Host { get; set; }

    public bool HasExited => Host!.HasExited;

    public int ExitCode => Host!.ExitCode;

    // The host's logs, its last lines kept in memory.
    public string Tail => Host?.Tail ?? "";

    // The folder on this machine the run's certificates are written to.
    public abstract string CertificateDirectory { get; }

    // The address the run's own servers listen on (mail, AI provider, webhook receiver), and the name the host reaches it by.
    public abstract IPAddress ServerAddress { get; }

    public abstract string ServerHost { get; }

    public abstract string SchemaPath { get; }

    public static HostProcess Create(DirectoryInfo root) =>
        Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } image ? new ContainerHost(root, image) : new PublishedHost(root);

    // Why the host cannot run here, which skips the cases, or null once it can.
    public abstract Task<string?> PrepareAsync(CancellationToken cancellationToken);

    public abstract string DataPath(string name);

    public abstract string CertificatePath(string path);

    public abstract string ListenUrl(Uri origin);

    public abstract Task<(int ExitCode, string Output, string Tail)> CommandAsync(
        IReadOnlyList<string> command, IReadOnlyDictionary<string, string?> environment, string? input, TimeSpan timeout, bool secretOutput, CancellationToken cancellationToken);

    public abstract void Start(Uri origin, IReadOnlyDictionary<string, string?> environment);

    public virtual void Dispose() => Host?.Dispose();
}
