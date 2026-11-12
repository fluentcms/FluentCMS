using FluentCMS.Core.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// Runs the seeders the host adds with cms.Seeding.Add, through the runner a start uses, with its claim and versions (4.16):
// a seeder applied at its version is skipped, so a second run does nothing.
internal sealed class SeedCommand : HostCommand
{
    public const string Only = "--name";

    public override string Name => "seed";

    public override string Description => "Runs the host's seeders that are not applied at their version, in registration order.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Only, "Runs only the seeder of this name, if it is not applied at its version.", "seeder"),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var runner = context.Services.GetRequiredService<SeedRunner>();
        var names = runner.Names;
        var only = context.Arguments.Value(Only);
        if (names.Count == 0)
        {
            if (only is not null)
            {
                throw new CommandException(CommandErrorCodes.UnknownSeeder, $"this host adds no seeder, so none is named '{only}'", CommandExitCodes.BadUsage);
            }

            await context.Output.WriteLineAsync("This host adds no seeder.");
            return CommandExitCodes.Success;
        }

        if (only is not null && !names.Contains(only, StringComparer.Ordinal))
        {
            throw new CommandException(CommandErrorCodes.UnknownSeeder, $"this host adds no seeder named '{only}'; its seeders are {string.Join(", ", names)}", CommandExitCodes.BadUsage);
        }

        var run = await runner.RunAsync(only, cancellationToken);
        foreach (var applied in run.Applied)
        {
            await context.Output.WriteLineAsync($"Applied {applied.Name} at version {applied.Version}.");
        }

        if (run.Skipped is { } reason)
        {
            throw new CommandException(CommandErrorCodes.SeedingClaimed, $"seeding stopped: {reason}; run 'fluentcms seed' again once it has finished");
        }

        if (run.Applied.Count == 0)
        {
            await context.Output.WriteLineAsync("Nothing to seed; every seeder is applied at its version.");
        }

        return CommandExitCodes.Success;
    }
}
