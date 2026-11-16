using System.Data.Common;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Events;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// Brings the content tables of a host whose types are data up to its schema: schema sync's engine, run by an operator
// rather than at start, on every provider. The system and plugin streams are migrate's, and a host with content
// migrations is refused, since its content tables belong to them.
internal sealed class SchemaApplyCommand : HostCommand
{
    public const string Script = "--script";
    public const string Output = "--output";
    public const string DryRun = "--dry-run";
    public const string AllowDestructive = "--allow-destructive";

    public override string Name => "schema apply";

    public override string Description => "Brings the content tables of a host whose types are data up to its schema, and records the schema in the schema history.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Script, "Writes the SQL for the database as it is now and applies nothing."),
        new(Output, "The file the script is written to; standard output when left out. Needs --script.", "file"),
        new(DryRun, "Prints the operations and applies nothing; exits with 1 when they would be refused."),
        new(AllowDestructive, "Applies a change that loses data, such as a dropped table or column or a changed column."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var script = context.Arguments.Has(Script);
        var file = context.Arguments.Value(Output);
        var dryRun = context.Arguments.Has(DryRun);
        if (file is not null && !script)
        {
            throw new CommandException(CommandErrorCodes.BadUsage, $"{Output} names the file of {Script}, so it needs {Script}", CommandExitCodes.BadUsage);
        }

        if (script && dryRun)
        {
            throw new CommandException(CommandErrorCodes.BadUsage, $"{Script} and {DryRun} both apply nothing; give one", CommandExitCodes.BadUsage);
        }

        var schema = SchemaSources.Host(context.Services);
        var runner = context.Services.GetRequiredService<MigrationRunner>();
        await RefuseAsync(runner, cancellationToken);

        // A run that applies holds the lock from the plan to the history row, so that a second run plans what the first left.
        var applying = !script && !dryRun;
        await using var held = applying ? await runner.LockContentSchemaAsync(cancellationToken) : null;
        using var guarded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, held?.Lost ?? CancellationToken.None);

        string? applied;
        SchemaApplyPlan plan;
        try
        {
            applied = await runner.GetAppliedSnapshotAsync(guarded.Token);
            plan = await runner.PlanContentSchemaAsync(applied, guarded.Token);
        }
        catch (Exception exception) when (LockLost(held, cancellationToken))
        {
            throw new CommandException(
                CommandErrorCodes.SchemaApplyFailed,
                $"{Lost(exception)} while the operations were planned, so nothing was applied; run 'fluentcms schema apply' again");
        }

        var steps = plan.Steps;
        var (report, unreadable) = SchemaHistory.Compare(applied, schema);
        if (!script)
        {
            await WritePlanAsync(context.Output, report, plan);
        }

        var refused = steps.Where(step => step.Kind == SchemaStepKind.Refused).Select(step => step.Description).ToList();
        if (refused.Count > 0)
        {
            throw new CommandException(
                CommandErrorCodes.SchemaChangeNotApplicable,
                $"schema apply cannot {string.Join("; ", refused)}, since the rows the table has would get no value; make the field optional or give it a default, and nothing was applied");
        }

        var losses = Losses(steps, report, unreadable, schema);
        if (losses.Count > 0)
        {
            var loss = $"the schema change loses data: {string.Join("; ", losses)}";
            if (!context.Arguments.Has(AllowDestructive))
            {
                throw new CommandException(CommandErrorCodes.DestructiveSchemaChange, $"{loss}; nothing was applied. Pass {AllowDestructive} to apply it.");
            }

            await context.Error.WriteLineAsync($"{CommandErrorCodes.DestructiveSchemaChange}: warning: {loss}; {(applying ? "applied" : "planned")} because of {AllowDestructive}.");
        }

        if (dryRun)
        {
            await context.Output.WriteLineAsync("Dry run; nothing was applied.");
            return CommandExitCodes.Success;
        }

        if (script)
        {
            return await WriteScriptAsync(context, runner, schema, plan, file, cancellationToken);
        }

        if (steps.Count > 0)
        {
            try
            {
                await runner.ApplyContentSchemaAsync(steps, guarded.Token);
            }
            // First, since a provider can end a command that the lost lock canceled with an exception of its own.
            catch (Exception exception) when (LockLost(held, cancellationToken))
            {
                throw new CommandException(
                    CommandErrorCodes.SchemaApplyFailed,
                    $"{Lost(exception)} while the operations were applied; their transaction was rolled back, except what SQLite committed before a table rebuild, which the next run leaves out, and the schema history keeps its last row; run 'fluentcms schema apply' again");
            }
            catch (DbException exception)
            {
                throw new CommandException(
                    CommandErrorCodes.SchemaApplyFailed,
                    $"the database refused an operation ({exception.GetType().Name}: {SecretScrubber.Of(exception.Message)}); its transaction was rolled back, except what SQLite committed before a table rebuild, which the next run leaves out, and the schema history keeps its last row");
            }
        }

        var hash = SchemaSnapshot.Of(schema).Hash;
        try
        {
            await runner.RecordSchemaAsync(guarded.Token);
        }
        catch (Exception exception) when (LockLost(held, cancellationToken))
        {
            throw new CommandException(
                CommandErrorCodes.SchemaNotRecorded,
                $"the content tables match schema {hash}, but {Lost(exception)} before the schema history was written; run 'fluentcms schema apply' again with the same options to record it");
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException)
        {
            throw new CommandException(
                CommandErrorCodes.SchemaNotRecorded,
                $"the content tables match schema {hash}, but the schema history could not be written ({exception.GetType().Name}); run 'fluentcms schema apply' again with the same options to record it");
        }

        await context.Output.WriteLineAsync(steps.Count == 0
            ? $"Nothing to apply; the content tables match schema {hash}, which the schema history records."
            : $"Applied {Count(steps.Count)}; the schema history records schema {hash}.");
        return CommandExitCodes.Success;
    }

    // The lock's Lost canceled the work, and not the caller.
    private static bool LockLost(IDistributedLockHandle? held, CancellationToken cancellationToken) =>
        held is { Lost.IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested;

    // Lost is canceled for every reason the lock goes: another holder took it, its lease ran out, or the host stopped. A
    // provider's own exception after it is named.
    private static string Lost(Exception exception) =>
        $"the lock {MigrationRunner.SchemaSyncLock} was lost{(exception is OperationCanceledException ? "" : $" ({exception.GetType().Name})")}";

    private static async Task RefuseAsync(MigrationRunner runner, CancellationToken cancellationToken)
    {
        var migrations = await runner.GetContentMigrationsAsync(cancellationToken);
        if (migrations.Count > 0)
        {
            throw new CommandException(
                CommandErrorCodes.ContentMigrationsExist,
                $"this host has content migrations ({string.Join(", ", migrations)}), so its content tables follow them, and a change applied beside them would leave their snapshot wrong; apply a schema change with a content migration instead");
        }

        var pending = await runner.GetPendingSystemAndPluginMigrationsAsync(cancellationToken);
        if (pending.Count > 0)
        {
            var streams = pending.GroupBy(migration => migration.Stream).Select(stream => $"stream {stream.Key} has not applied {string.Join(", ", stream.Select(migration => migration.Id))}");
            throw new CommandException(CommandErrorCodes.StreamsNotMigrated, $"{string.Join("; ", streams)}; run 'fluentcms migrate' first, since schema apply changes only the content tables");
        }
    }

    private static async Task WritePlanAsync(TextWriter output, SchemaDiffReport? report, SchemaApplyPlan plan)
    {
        if (report is { HasChanges: true })
        {
            await output.WriteLineAsync("Changes from the applied schema:");
            await report.WriteTextAsync(output);
        }

        if (plan.Steps.Count > 0)
        {
            await output.WriteLineAsync("Operations:");
            foreach (var step in plan.Steps)
            {
                await output.WriteLineAsync($"  {PrintableText.Escape(step.Description)}{Mark(step)}");
            }
        }

        if (plan.LeftAlone.Count > 0)
        {
            await output.WriteLineAsync("Left alone:");
            foreach (var table in plan.LeftAlone)
            {
                await output.WriteLineAsync($"  {PrintableText.Escape(table)}");
            }
        }
    }

    private static string Mark(SchemaApplyStep step) => step.Kind switch
    {
        SchemaStepKind.Destructive => SchemaDiffReport.DestructiveMark,
        SchemaStepKind.Refused => " [refused]",
        _ => "",
    };

    // What the tables lose, and what the history says is lost although the tables keep it.
    private static List<string> Losses(IReadOnlyList<SchemaApplyStep> steps, SchemaDiffReport? report, bool unreadable, SchemaModel schema) =>
        [.. steps.Where(step => step.IsDestructive).Select(step => step.Description), .. SchemaHistory.Losses(report, unreadable, schema)];

    private static async Task<int> WriteScriptAsync(CommandContext context, MigrationRunner runner, SchemaModel schema, SchemaApplyPlan plan, string? file, CancellationToken cancellationToken)
    {
        IEnumerable<string> header =
        [
            $"-- fluentcms schema apply: the content tables to schema {SchemaSnapshot.Of(schema).Hash}.",
            .. plan.Steps.Count == 0 ? ["-- Nothing to apply."] : plan.Steps.Select(step => Comment(step.Description)),
            .. plan.LeftAlone.Select(table => Comment($"Left alone: {table}")),
        ];
        var sql = string.Join(Environment.NewLine, header) + Environment.NewLine + runner.ScriptContentSchema(plan.Steps);

        if (file is null)
        {
            await context.Output.WriteAsync(sql);
            return CommandExitCodes.Success;
        }

        await SchemaSources.WriteFileAsync(file, sql, cancellationToken);
        await context.Output.WriteLineAsync($"Wrote {Path.GetFullPath(file)}");
        return CommandExitCodes.Success;
    }

    // A description holds names read from the database, and a line break in one would end the comment and run the rest
    // of the line as SQL.
    private static string Comment(string text) => "-- " + PrintableText.Escape(text);

    private static string Count(int count) => count == 1 ? "1 change" : $"{count} changes";
}
