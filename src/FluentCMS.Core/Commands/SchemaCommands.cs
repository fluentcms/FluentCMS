using System.Text;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The canonical snapshot of 4.2, the bytes GET /schema/snapshot answers for a key that may read every type.
internal sealed class SchemaSnapshotCommand : HostCommand
{
    public const string Output = "--output";

    public override string Name => "schema snapshot";

    public override string Description => "Writes the canonical snapshot of the host's schema, whose SHA-256 is the schema hash.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Output, "The file to write; standard output when left out.", "file"),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var snapshot = SchemaSnapshot.Of(SchemaSources.Host(context.Services));
        if (context.Arguments.Value(Output) is not { } file)
        {
            await SchemaSources.WriteBytesAsync(context.Output, Encoding.UTF8.GetBytes(snapshot.Json), cancellationToken);
            return CommandExitCodes.Success;
        }

        await SchemaSources.WriteFileAsync(file, snapshot.Json, cancellationToken);
        await context.Output.WriteLineAsync($"Wrote {Path.GetFullPath(file)}");
        return CommandExitCodes.Success;
    }
}

// Builds the host's schema, or a schema file with the host's field types, and prints every FCMS12xx error.
internal sealed class SchemaValidateCommand : HostCommand
{
    public const string Schema = "--schema";

    public override string Name => "schema validate";

    public override string Description => "Builds the host's schema, or a schema file, and prints every error; exits with 1 when there is one.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Schema, "A schema file to check in place of the host's schema.", "file"),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var file = context.Arguments.Value(Schema);
        var what = file ?? "the host's schema";
        SchemaModel schema;
        try
        {
            schema = file is null
                ? context.Services.GetRequiredService<SchemaModel>()
                : SchemaFiles.Read(file, context.Services.GetRequiredService<IFieldTypeRegistry>());
        }
        catch (SchemaValidationException exception)
        {
            return await ReportAsync(context.Output, context.Error, what, exception.Errors);
        }
        catch (Exception exception) when (file is not null && SchemaFiles.IsUnreadable(exception))
        {
            throw new CommandException(CommandErrorCodes.SchemaFileUnreadable, $"{file} could not be read: {exception.Message}");
        }

        await context.Output.WriteLineAsync(Valid(file ?? "The host's schema", schema));
        return CommandExitCodes.Success;
    }

    // The errors are what the command answers, so they go to standard output, and the verdict to standard error.
    internal static async Task<int> ReportAsync(TextWriter output, TextWriter error, string what, IReadOnlyList<SchemaError> errors)
    {
        foreach (var item in errors)
        {
            await output.WriteLineAsync(item.ToString());
        }

        await error.WriteLineAsync($"{CommandErrorCodes.SchemaInvalid}: {what} has {SchemaSources.Errors(errors.Count)}");
        return CommandExitCodes.Failure;
    }

    internal static string Valid(string what, SchemaModel schema)
    {
        var types = schema.ContentTypes.Count + schema.Components.Count + schema.Blocks.Count;
        return $"{what} is valid: {(types == 1 ? "1 type" : $"{types} types")}, schema hash {SchemaSnapshot.Of(schema).Hash}.";
    }
}

// Compares two schemas in the words of the drift log of 4.2, from the one deployed to the new one: the applied history or
// a schema file to the host's schema, or the applied history to a schema file. --url is the tool's, which reads the
// instance itself and diffs there.
internal sealed class SchemaDiffCommand : HostCommand
{
    public const string Applied = "--applied";
    public const string Schema = "--schema";
    public const string Json = "--json";

    public override string Name => "schema diff";

    public override string Description => "Compares the schema in the newest row of the schema history, or a schema file, with the host's schema; exits with 1 when they differ.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Applied, "Compares from the schema in the newest row of the schema history of the host's database."),
        new(Schema, "A schema file: what is compared from, or with --applied, what is compared to in place of the host's schema.", "file"),
        new(Json, "Writes the report as one line of JSON: {\"changes\":[...],\"possibleRenames\":[...]}."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var applied = context.Arguments.Has(Applied);
        var file = context.Arguments.Value(Schema);
        if (!applied && file is null)
        {
            throw new CommandException(CommandErrorCodes.BadUsage, $"'fluentcms schema diff' compares from {Applied}, {Schema} <file> or, in the tool, --url <url>", CommandExitCodes.BadUsage);
        }

        string from, fromLabel, to, toLabel;
        if (applied)
        {
            (from, fromLabel) = (await AppliedAsync(context.Services, cancellationToken), "the applied schema");
            (to, toLabel) = file is null ? (HostJson(context.Services), "the host's schema") : (FileJson(context.Services, file), file);
        }
        else
        {
            (from, fromLabel) = (FileJson(context.Services, file!), file!);
            (to, toLabel) = (HostJson(context.Services), "the host's schema");
        }

        SchemaDiffReport report;
        try
        {
            report = SchemaSnapshotDiff.Report(from, to);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new CommandException(CommandErrorCodes.AppliedSchemaUnreadable, "the snapshot in the newest row of the schema history is not in the form of a schema, so it cannot be compared");
        }

        return await SchemaSources.WriteDiffAsync(report, context.Arguments.Has(Json), fromLabel, toLabel, context.Output, context.Error);
    }

    private static string HostJson(IServiceProvider services) => SchemaSnapshot.Of(SchemaSources.Host(services)).Json;

    private static string FileJson(IServiceProvider services, string file) =>
        SchemaSnapshot.Of(SchemaSources.File(file, services.GetRequiredService<IFieldTypeRegistry>())).Json;

    private static async Task<string> AppliedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        string? snapshot;
        try
        {
            snapshot = await services.GetRequiredService<MigrationRunner>().GetAppliedSnapshotAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CommandException(CommandErrorCodes.AppliedSchemaUnreadable, $"the schema history of the host's database could not be read ({exception.GetType().Name}); run 'fluentcms migrate' to create it");
        }

        return snapshot ?? throw new CommandException(CommandErrorCodes.NoAppliedSchema, "the schema history of the host's database is empty; run 'fluentcms migrate' to apply the schema and record it");
    }
}
