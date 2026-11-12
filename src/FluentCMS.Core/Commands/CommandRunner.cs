using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Commands;

internal static class CommandRunner
{
    public const string Prefix = "fluentcms";

    // Null when the arguments name no command; otherwise the exit code: 0, 1 for a failure, 2 for bad usage.
    public static Task<int?> RunAsync(IServiceProvider services, IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        RunAsync(services, args, output, error, CommandRegistry.Commands, cancellationToken);

    // With the commands given, which a test names.
    internal static async Task<int?> RunAsync(IServiceProvider services, IReadOnlyList<string> args, TextWriter output, TextWriter error, IReadOnlyList<HostCommand> commands, CancellationToken cancellationToken)
    {
        if (args is not [Prefix, ..])
        {
            return null;
        }

        var rest = args.Skip(1).ToList();

        var parsed = CommandParser.Parse(commands, rest);
        if (parsed.Help)
        {
            await output.WriteLineAsync(parsed.Command?.Usage ?? CommandRegistry.UsageOf(commands));
            return CommandExitCodes.Success;
        }

        if (parsed is not { Command: { } command, Arguments: { } arguments })
        {
            await error.WriteLineAsync($"{CommandErrorCodes.BadUsage}: {parsed.Error}.");
            await error.WriteLineAsync();
            await error.WriteLineAsync(parsed.Command?.Usage ?? CommandRegistry.UsageOf(commands));
            return CommandExitCodes.BadUsage;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CommandRunner).FullName!);
        CommandLog.Running(logger, command.Name);

        int exitCode;
        await using (var scope = services.CreateAsyncScope())
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
            using var operation = SystemOperation.Begin(accessor, Origin.Cli, new SiteId(DefaultSite.Id));
            try
            {
                exitCode = await command.RunAsync(new CommandContext(scope.ServiceProvider, arguments, output, error), cancellationToken);
            }
            catch (CommandException exception)
            {
                await error.WriteLineAsync(exception.Message);
                if (exception.ExitCode == CommandExitCodes.BadUsage)
                {
                    await error.WriteLineAsync();
                    await error.WriteLineAsync(command.Usage);
                }

                exitCode = exception.ExitCode;
            }
            catch (FluentCmsConfigurationException exception)
            {
                await error.WriteLineAsync(exception.Message);
                exitCode = CommandExitCodes.Failure;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A cancellation too, so that the process still ends with 1 and a message rather than a crash. The log keeps
                // the stack, and both leave out what looks like a secret.
                CommandLog.Failed(logger, command.Name, exception.GetType().Name, SecretScrubber.Of(exception.ToString()));
                await error.WriteLineAsync($"{CommandErrorCodes.CommandFailed}: 'fluentcms {command.Name}' failed: {exception.GetType().Name}: {SecretScrubber.Of(exception.Message)}");
                exitCode = CommandExitCodes.Failure;
            }
        }

        CommandLog.Ended(logger, command.Name, exitCode);
        return exitCode;
    }
}
