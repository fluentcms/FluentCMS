using System.Data.Common;
using System.Text.Json;
using FluentCMS.Core.Events;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
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

        var steps = await runner.PlanContentSchemaAsync(guarded.Token);
        var (report, unreadable) = await ReportAsync(runner, schema, guarded.Token);
        if (!script)
        {
            await WritePlanAsync(context.Output, report, steps);
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
            return await WriteScriptAsync(context, runner, schema, steps, file, cancellationToken);
        }

        if (steps.Count > 0)
        {
            try
            {
                await runner.ApplyContentSchemaAsync(steps, guarded.Token);
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

    // From the newest row of the schema history to the host's schema, which tells a renamed field and a changed kind that
    // the tables alone do not. Null while the history is empty; unreadable when its snapshot is not in the form of a schema.
    private static async Task<(SchemaDiffReport? Report, bool Unreadable)> ReportAsync(MigrationRunner runner, SchemaModel schema, CancellationToken cancellationToken)
    {
        if (await runner.GetAppliedSnapshotAsync(cancellationToken) is not { } applied)
        {
            return (null, false);
        }

        try
        {
            return (SchemaSnapshotDiff.Report(applied, SchemaSnapshot.Of(schema).Json), false);
        }
        catch (JsonException)
        {
            return (null, true);
        }
    }

    private static async Task WritePlanAsync(TextWriter output, SchemaDiffReport? report, IReadOnlyList<SchemaApplyStep> steps)
    {
        if (report is { HasChanges: true })
        {
            await output.WriteLineAsync("Changes from the applied schema:");
            await report.WriteTextAsync(output);
        }

        if (steps.Count > 0)
        {
            await output.WriteLineAsync("Operations:");
            foreach (var step in steps)
            {
                await output.WriteLineAsync($"  {step.Description}{Mark(step)}");
            }
        }
    }

    private static string Mark(SchemaApplyStep step) => step.Kind switch
    {
        SchemaStepKind.Destructive => SchemaDiffReport.DestructiveMark,
        SchemaStepKind.Refused => " [refused]",
        _ => "",
    };

    // What the tables lose, and what the history says is lost although the tables keep it: a removed field or type, a
    // changed kind, and a renamed field whose values no column of its own holds.
    private static List<string> Losses(IReadOnlyList<SchemaApplyStep> steps, SchemaDiffReport? report, bool unreadable, SchemaModel schema)
    {
        List<string> losses = [.. steps.Where(step => step.IsDestructive).Select(step => step.Description)];
        if (report is not null)
        {
            losses.AddRange(report.Destructive.Select(change => change.ToString()));
            losses.AddRange(report.Changes
                .Where(change => change.Kind == SchemaChangeKind.FieldRenamed && !HasColumns(change, schema))
                .Select(change => $"{change}, whose values are kept in JSON or as relations under '{change.FormerField}', which schema apply does not rename"));
        }

        if (unreadable)
        {
            losses.Add("the snapshot in the newest row of the schema history is not in the form of a schema, so renamed fields and changed kinds cannot be checked");
        }

        return losses;
    }

    // A field of a content type that is a column, or single components of columns, whose columns the plan renames. A
    // component or block field may be kept in JSON, and a relation is rows of cms_reference.
    private static bool HasColumns(SchemaChange change, SchemaModel schema)
    {
        if (change.Family != SchemaSnapshotDiff.ContentFamily || schema.FindContentType(change.Type) is not { } type)
        {
            return false;
        }

        var leaves = FieldStorage.ByPath(schema, type)
            .Where(leaf => leaf.Key == change.Field || leaf.Key.StartsWith($"{change.Field}.", StringComparison.Ordinal))
            .ToList();
        return leaves.Count > 0 && leaves.All(leaf => leaf.Value.Kind != FieldStorageKind.Reference);
    }

    private static async Task<int> WriteScriptAsync(CommandContext context, MigrationRunner runner, SchemaModel schema, IReadOnlyList<SchemaApplyStep> steps, string? file, CancellationToken cancellationToken)
    {
        IEnumerable<string> header =
        [
            $"-- fluentcms schema apply: the content tables to schema {SchemaSnapshot.Of(schema).Hash}.",
            .. steps.Count == 0 ? ["-- Nothing to apply."] : steps.Select(step => $"-- {step.Description}"),
        ];
        var sql = string.Join(Environment.NewLine, header) + Environment.NewLine + runner.ScriptContentSchema(steps);

        if (file is null)
        {
            await context.Output.WriteAsync(sql);
            return CommandExitCodes.Success;
        }

        await SchemaSources.WriteFileAsync(file, sql, cancellationToken);
        await context.Output.WriteLineAsync($"Wrote {Path.GetFullPath(file)}");
        return CommandExitCodes.Success;
    }

    private static string Count(int count) => count == 1 ? "1 change" : $"{count} changes";
}
