using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// Applies every stream whatever FluentCms:Database:MigrateOnStartup says, as an init container or a deploy step does.
internal sealed class MigrateCommand : HostCommand
{
    public const string Check = "--check";

    public override string Name => "migrate";

    public override string Description => "Applies the pending migrations of every stream, in the order system, plugins, content, and records the schema in the schema history.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Check, "Lists the pending migrations and applies nothing; exits with 1 when any is pending."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var runner = context.Services.GetRequiredService<MigrationRunner>();
        if (context.Arguments.Has(Check))
        {
            var pending = await runner.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Count == 0)
            {
                await context.Output.WriteLineAsync("No migration is pending.");
                return CommandExitCodes.Success;
            }

            await WriteByStreamAsync(context.Output, pending);
            await context.Error.WriteLineAsync($"{CommandErrorCodes.MigrationsPending}: {Count(pending.Count)} pending; run 'fluentcms migrate' to apply them");
            return CommandExitCodes.Failure;
        }

        // What the startup of a host checks before it migrates.
        context.Services.GetService<PluginRegistry>()?.Contributions.CheckContentTables(context.Services.GetRequiredService<SchemaModel>());

        var applied = await runner.MigrateAsync(cancellationToken);
        if (applied.Count == 0)
        {
            await context.Output.WriteLineAsync("Nothing to apply; every stream is up to date.");
            return CommandExitCodes.Success;
        }

        await WriteByStreamAsync(context.Output, applied);
        await context.Output.WriteLineAsync($"Applied {Count(applied.Count)}.");
        return CommandExitCodes.Success;
    }

    internal static string Count(int count) => count == 1 ? "1 migration" : $"{count} migrations";

    private static async Task WriteByStreamAsync(TextWriter output, IReadOnlyList<StreamMigration> migrations)
    {
        foreach (var stream in migrations.GroupBy(migration => migration.Stream))
        {
            await output.WriteLineAsync(stream.Key);
            foreach (var migration in stream)
            {
                await output.WriteLineAsync($"  {migration.Id}");
            }
        }
    }
}
