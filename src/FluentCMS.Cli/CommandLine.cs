using System.CommandLine;
using System.CommandLine.Help;
using System.Text.Json;

namespace FluentCMS.Cli;

// The command tree of `fluentcms`. A project command either runs inside the host, which the tool builds and starts with
// "fluentcms <command>" (see IHostLauncher), or, for `migrations add` and `remove`, in the tool's own process, because
// they need EF Core's design-time package, which a host does not get. Exit codes: 0, 1 for a failure, 2 for bad usage.
internal sealed class CommandLine(TextWriter output, TextWriter error, IHostLauncher launcher)
{
    public const string Description = "The fluentcms tool. Project commands build the host project and run inside it, with its configuration and plugins.";

    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IHostLauncher? launcher = null, CancellationToken cancellationToken = default) =>
        new CommandLine(output, error, launcher ?? new ProcessHostLauncher()).RunAsync(args, cancellationToken);

    public static Command Tree() => new CommandLine(TextWriter.Null, TextWriter.Null, new ProcessHostLauncher()).Build();

    private async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var result = Build().Parse(args);
        if (result.Errors.Count > 0)
        {
            // The parser's own action writes the errors and the help of the command.
            await result.InvokeAsync(new InvocationConfiguration { Output = error, Error = error }, cancellationToken);
            return ExitCodes.BadUsage;
        }

        // The tool waits for a host it started, which gets Ctrl+C itself, so the parser does not end the process first.
        return await result.InvokeAsync(
            new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false, ProcessTerminationTimeout = null },
            cancellationToken);
    }

    // A command named fluentcms rather than a RootCommand, which would take its name from the assembly, FluentCMS.Cli.
    private Command Build() => new("fluentcms", Description)
    {
        new HelpOption(),
        new VersionOption(),
        Migrate(),
        new Command("migrations", "Content migrations of the host, and the migrations of every stream.")
        {
            MigrationsAdd(),
            MigrationsRemove(),
            MigrationsList(),
            MigrationsScript(),
        },
    };

    private Command Migrate()
    {
        var check = new Option<bool>("--check") { Description = "Lists the pending migrations and applies nothing; exits with 1 when any is pending." };
        var command = new Command("migrate", "Applies the pending migrations of every stream, in the order system, plugins, content, and records the schema history.") { check };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(result, project, ["migrate", .. Flag(result, check)], cancellationToken));
        return command;
    }

    private Command MigrationsList()
    {
        var json = new Option<bool>("--json") { Description = "Writes the list as one line of JSON." };
        var command = new Command("list", "Lists every stream with its applied and pending migrations.") { json };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(result, project, ["migrations", "list", .. Flag(result, json)], cancellationToken));
        return command;
    }

    private Command MigrationsScript()
    {
        var stream = new Option<string>("--stream") { Description = "The stream: system, a plugin's name or content. Every stream when left out.", HelpName = "name" };
        var from = new Option<string>("--from") { Description = "The migration the database has; 0 is before the first. Needs --stream.", HelpName = "migration" };
        var to = new Option<string>("--to") { Description = "The migration to bring it to; the newest when left out. Needs --stream.", HelpName = "migration" };
        var idempotent = new Option<bool>("--idempotent") { Description = "SQL that checks the history and applies only what is missing (PostgreSQL and SQL Server)." };
        var file = new Option<string>("--output") { Description = "The file to write; standard output when left out.", HelpName = "file" };
        var command = new Command("script", "Writes the SQL of the host's provider for a stream, for a database that only an administrator may change.")
        {
            stream, from, to, idempotent, file,
        };

        command.Validators.Add(result =>
        {
            if (result.GetValue(stream) is null && (result.GetValue(from) is not null || result.GetValue(to) is not null))
            {
                result.AddError("--from and --to name a migration of one stream, so they need --stream.");
            }
        });

        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(
            result,
            project,
            [
                "migrations", "script",
                .. Value(result, stream), .. Value(result, from), .. Value(result, to), .. Flag(result, idempotent),

                // The host runs in the project directory, and the path is the caller's.
                .. result.GetValue(file) is { } path ? ["--output", Path.GetFullPath(path)] : Array.Empty<string>(),
            ],
            cancellationToken));
        return command;
    }

    private Command MigrationsAdd()
    {
        var name = new Argument<string>("name") { Description = "The name of the migration, such as AddSubtitle." };
        var command = new Command("add", "Generates a content migration from the host's schema and provider, in Migrations/Content of the project.") { name };
        var project = ProjectOptions.AddTo(command);
        command.SetAction(result => Guard(() =>
        {
            using var host = OpenInTool(result, project);
            foreach (var file in host.AddMigration(result.GetValue(name)!))
            {
                output.WriteLine($"Created {file}");
            }

            return ExitCodes.Success;
        }));
        return command;
    }

    private Command MigrationsRemove()
    {
        var force = new Option<bool>("--force") { Description = "Removes the migration without asking the host whether its database has it." };
        var command = new Command("remove", "Removes the newest content migration and puts the snapshot back, unless the database has it.") { force };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var target = project.Resolve(result, error);
            using var host = OpenInTool(target, project.EnvironmentOf(result));
            var newest = host.NewestMigration() ?? throw new CliException(CliErrorCodes.NoContentMigration, $"{target.Name} has no content migration to remove.");

            if (!result.GetValue(force) && (await AppliedContentMigrationsAsync(target, project.EnvironmentOf(result), newest, cancellationToken)).Contains(newest))
            {
                throw new CliException(
                    CliErrorCodes.MigrationApplied,
                    $"{newest} is applied to the host's database, so removing it would leave the database ahead of the code; add a migration that undoes it instead, or pass --force to remove the files and leave the database as it is.");
            }

            var files = host.RemoveMigration();
            foreach (var file in new[] { files.MigrationFile, files.MetadataFile }.OfType<string>())
            {
                await output.WriteLineAsync($"Removed {file}");
            }

            if (files.SnapshotFile is { } snapshot)
            {
                await output.WriteLineAsync(File.Exists(snapshot) ? $"Restored {snapshot}" : $"Removed {snapshot}");
            }

            return ExitCodes.Success;
        }));
        return command;
    }

    private Task<int> HandOverAsync(ParseResult result, ProjectOptions project, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        GuardAsync(() => launcher.RunAsync(project.Resolve(result, error), project.EnvironmentOf(result), arguments, output, error, cancellationToken));

    // The content stream's applied migrations, from `migrations list --json` inside the host, which has the connection.
    private async Task<IReadOnlyList<string>> AppliedContentMigrationsAsync(HostTarget target, string? environment, string newest, CancellationToken cancellationToken)
    {
        using var listed = new StringWriter();
        using var failed = new StringWriter();
        var exitCode = await launcher.RunAsync(target, environment, ["migrations", "list", "--json"], listed, failed, cancellationToken);

        var line = listed.ToString().Split('\n').Select(text => text.Trim()).LastOrDefault(text => text.StartsWith("{\"streams\"", StringComparison.Ordinal));
        if (exitCode != ExitCodes.Success || line is null)
        {
            throw new CliException(
                CliErrorCodes.AppliedMigrationsUnknown,
                $"the host could not list its migrations (exit code {exitCode}), so it is not known whether {newest} is applied; pass --force to remove it without asking.{Environment.NewLine}{failed.ToString().Trim()}");
        }

        using var json = JsonDocument.Parse(line);
        return [.. json.RootElement.GetProperty("streams").EnumerateArray()
            .Where(stream => stream.GetProperty("name").GetString() == "content")
            .SelectMany(stream => stream.GetProperty("applied").EnumerateArray())
            .Select(id => id.GetString()!)];
    }

    private HostProject OpenInTool(ParseResult result, ProjectOptions project) => OpenInTool(project.Resolve(result, error), project.EnvironmentOf(result));

    // The host's design-time factory runs in this process, so it sees the environment as the host would.
    private static HostProject OpenInTool(HostTarget target, string? environment)
    {
        if (environment is not null)
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
        }

        return HostProject.Open(target);
    }

    private int Guard(Func<int> run)
    {
        try
        {
            return run();
        }
        catch (Exception exception) when (IsReported(exception))
        {
            return Report(exception);
        }
    }

    private async Task<int> GuardAsync(Func<Task<int>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception exception) when (IsReported(exception))
        {
            return Report(exception);
        }
    }

    private static bool IsReported(Exception exception) =>
        exception is InvalidOperationException or IOException or ArgumentException or BadImageFormatException or UnauthorizedAccessException or JsonException or KeyNotFoundException;

    private int Report(Exception exception)
    {
        error.WriteLine(exception.Message);
        return exception is CliException cli ? cli.ExitCode : ExitCodes.Failure;
    }

    private static string[] Flag(ParseResult result, Option<bool> option) => result.GetValue(option) ? [option.Name] : [];

    private static string[] Value(ParseResult result, Option<string> option) => result.GetValue(option) is { } value ? [option.Name, value] : [];
}
