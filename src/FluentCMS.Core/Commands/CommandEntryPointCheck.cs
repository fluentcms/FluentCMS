using FluentCMS.Core.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Commands;

// The arguments of the process, without the program. A test replaces them.
internal sealed record ProcessArguments(IReadOnlyList<string> Values)
{
    public static ProcessArguments Current() => new([.. Environment.GetCommandLineArgs().Skip(1)]);
}

// A host started with "fluentcms <command>" that never called RunFluentCmsCommandAsync would go on to serve, migrating
// and seeding on the way. Registered first, so that it stops the host before anything is applied.
internal sealed class CommandEntryPointCheck(ProcessArguments arguments) : IHostedLifecycleService
{
    public const string EntryPointLine = "if (await app.RunFluentCmsCommandAsync(args) is { } exitCode) return exitCode;";

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (arguments.Values is not [CommandRunner.Prefix, ..])
        {
            return Task.CompletedTask;
        }

        // The words only: an option's value is the caller's and is not repeated.
        var command = string.Join(' ', arguments.Values.TakeWhile(argument => !argument.StartsWith('-')));
        throw new FluentCmsConfigurationException(
            CommandErrorCodes.NoEntryPoint,
            $"the process was started with '{command}', but this host does not run commands; add '{EntryPointLine}' to Program.cs between Build() and Run()");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
