using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Commands;

public static class FluentCmsCommandExtensions
{
    // The command entry point, called between Build() and Run():
    //   if (await app.RunFluentCmsCommandAsync(args) is { } exitCode) return exitCode;
    // Null when the arguments do not start with "fluentcms". Otherwise the command runs, before any hosted service has
    // started, so nothing migrates, seeds, runs jobs or listens, and the result is the exit code of the process.
    public static Task<int?> RunFluentCmsCommandAsync(this IHost host, string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(args);

        return CommandRunner.RunAsync(host.Services, args, Console.Out, Console.Error, cancellationToken);
    }
}
