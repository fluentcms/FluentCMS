using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The SQL of the host's provider, for a database that only an administrator may change. Nothing is opened or applied.
internal sealed class MigrationsScriptCommand : HostCommand
{
    public const string Stream = "--stream";
    public const string From = "--from";
    public const string To = "--to";
    public const string Idempotent = "--idempotent";
    public const string Output = "--output";

    private const string Initial = "0";

    public override string Name => "migrations script";

    public override string Description => "Writes the SQL of the host's provider for a stream, or for every stream in the order they apply.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Stream, "The stream: system, a plugin's name or content. Every stream when left out.", "name"),
        new(From, "The migration the database has; 0 is before the first. Needs --stream.", "migration"),
        new(To, "The migration to bring it to; the newest when left out. Needs --stream.", "migration"),
        new(Idempotent, "SQL that checks the history and applies only what is missing (PostgreSQL and SQL Server)."),
        new(Output, "The file to write; standard output when left out.", "file"),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var runner = context.Services.GetRequiredService<MigrationRunner>();
        var stream = context.Arguments.Value(Stream);
        var from = context.Arguments.Value(From);
        var to = context.Arguments.Value(To);

        if (stream is null && (from is not null || to is not null))
        {
            throw new CommandException(CommandErrorCodes.BadUsage, $"{From} and {To} name a migration of one stream, so they need {Stream}", CommandExitCodes.BadUsage);
        }

        if (stream is not null && !runner.Streams.Contains(stream))
        {
            throw new CommandException(CommandErrorCodes.UnknownStream, $"this host has no stream '{stream}'; its streams are {string.Join(", ", runner.Streams)}", CommandExitCodes.BadUsage);
        }

        if (stream is not null)
        {
            var migrations = runner.MigrationsOf(stream);
            if (new[] { from, to }.OfType<string>().FirstOrDefault(id => id != Initial && !migrations.Contains(id)) is { } unknown)
            {
                throw new CommandException(CommandErrorCodes.UnknownMigration, $"stream {stream} has no migration '{unknown}'", CommandExitCodes.BadUsage);
            }
        }

        var idempotent = context.Arguments.Has(Idempotent);
        var sql = new List<string>();
        try
        {
            foreach (var name in stream is null ? runner.Streams.Where(name => runner.MigrationsOf(name).Count > 0) : [stream])
            {
                sql.Add($"-- stream {name}{Environment.NewLine}{await runner.ScriptAsync(name, from, to, idempotent, cancellationToken)}");
            }
        }
        catch (NotSupportedException exception) when (idempotent)
        {
            throw new CommandException(CommandErrorCodes.ScriptNotSupported, $"the provider of this host writes no idempotent script ({exception.Message}); leave out {Idempotent} and give {From}");
        }

        var script = string.Join(Environment.NewLine, sql);
        var file = context.Arguments.Value(Output);
        if (file is null)
        {
            await context.Output.WriteAsync(script);
            return CommandExitCodes.Success;
        }

        try
        {
            await File.WriteAllTextAsync(file, script, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CommandException(CommandErrorCodes.OutputNotWritten, $"{file} could not be written: {exception.Message}");
        }

        await context.Output.WriteLineAsync($"Wrote {Path.GetFullPath(file)}");
        return CommandExitCodes.Success;
    }
}
