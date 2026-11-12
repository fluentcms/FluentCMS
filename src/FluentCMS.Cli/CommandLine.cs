using System.CommandLine;
using System.CommandLine.Help;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Schema;
using FluentCMS.Diagnostics;
using FluentCMS.Schema;

namespace FluentCMS.Cli;

// The command tree of `fluentcms`. A project command either runs inside the host, which the tool builds and starts with
// "fluentcms <command>" (see IHostLauncher), or, for `migrations add` and `remove`, in the tool's own process, because
// they need EF Core's design-time package, which a host does not get. Exit codes: 0, 1 for a failure, 2 for bad usage.
internal sealed class CommandLine(TextWriter output, TextWriter error, IHostLauncher launcher, Func<Instance, HttpClient>? connect = null)
{
    public const string Description = "The fluentcms tool. Project commands build the host project and run inside it, with its configuration and plugins.";

    private readonly Func<Instance, HttpClient> _connect = connect ?? (_ => new HttpClient());

    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IHostLauncher? launcher = null, CancellationToken cancellationToken = default) =>
        RunAsync(args, output, error, launcher, connect: null, cancellationToken);

    // With the client that reaches an instance of --url, which a test names.
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IHostLauncher? launcher, Func<Instance, HttpClient>? connect, CancellationToken cancellationToken = default) =>
        new CommandLine(output, error, launcher ?? new ProcessHostLauncher(), connect).RunAsync(args, cancellationToken);

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
        new Command("schema", "The host's schema: its snapshot, its errors, and what differs from an instance, a schema file or the applied history.")
        {
            Snapshot(),
            Validate(),
            Diff(),
        },
        Doctor(),
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
        var allow = new Option<bool>("--allow-destructive") { Description = "Generates the migration even when it loses data, such as a removed type or field, a changed field type or a renamed field." };
        var command = new Command("add", "Prints what the schema change is, then generates a content migration from the host's schema and provider, in Migrations/Content of the project.") { name, allow };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            using var host = OpenInTool(result, project);
            await ReviewAsync(host.ReviewMigration(), result.GetValue(allow));
            foreach (var file in host.AddMigration(result.GetValue(name)!))
            {
                await output.WriteLineAsync($"Created {file}");
            }

            return ExitCodes.Success;
        }));
        return command;
    }

    // The schema change the migration carries, in the words of `schema diff`. A change that loses data is refused unless
    // allowed, and so is a rename: the generated migration drops the old column and adds the new one, since it does not
    // read [FormerNames], so the values would be lost all the same.
    private async Task ReviewAsync(SchemaDiffReport? review, bool allowDestructive)
    {
        if (review is null)
        {
            const string Unchecked = "the newest content migration records no schema, so what this migration changes cannot be checked for lost data";
            if (!allowDestructive)
            {
                throw new CliException(CliErrorCodes.MigrationUnchecked, $"{Unchecked}; read the generated migration before applying it, and pass --allow-destructive to generate it.");
            }

            await error.WriteLineAsync($"{CliErrorCodes.MigrationUnchecked}: warning: {Unchecked}; the next migration is checked.");
            return;
        }

        await review.WriteTextAsync(output);

        var renamed = review.Changes.Where(change => change.Kind == SchemaChangeKind.FieldRenamed).ToList();
        List<SchemaChange> losing = [.. review.Destructive, .. renamed];
        if (losing.Count == 0)
        {
            return;
        }

        var loss = $"the migration loses data: {string.Join("; ", losing)}"
            + (renamed.Count == 0 ? "" : "; a renamed field is still a dropped column and an added one in the generated migration, which does not carry its values over, so edit the migration to rename the column");
        if (!allowDestructive)
        {
            throw new CliException(CliErrorCodes.DestructiveMigration, $"{loss}; nothing was written. Pass --allow-destructive to generate it.");
        }

        await error.WriteLineAsync($"{CliErrorCodes.DestructiveMigration}: warning: {loss}; generated because of --allow-destructive.");
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

    private Command Snapshot()
    {
        var file = new Option<string>("--output") { Description = "The file to write; standard output when left out.", HelpName = "file" };
        var command = new Command("snapshot", "Writes the canonical snapshot of the host's schema, or with --url of an instance's, whose SHA-256 is the schema hash.") { file };
        var project = ProjectOptions.AddTo(command);
        var instance = InstanceOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var path = result.GetValue(file) is { } given ? Path.GetFullPath(given) : null;
            if (instance.Resolve(result) is not { } target)
            {
                return await HandOverAsync(result, project, ["schema", "snapshot", .. path is null ? Array.Empty<string>() : ["--output", path]], cancellationToken);
            }

            var snapshot = await InstanceSnapshotAsync(target, cancellationToken);
            if (path is null)
            {
                await SchemaSources.WriteBytesAsync(output, snapshot, cancellationToken);
                return ExitCodes.Success;
            }

            await WriteFileAsync(path, snapshot, cancellationToken);
            await output.WriteLineAsync($"Wrote {path}");
            return ExitCodes.Success;
        }));
        return command;
    }

    // A schema file is checked here, with core's field types, unless --project or --assembly names a host to check it
    // with its own, plugins' types included.
    private Command Validate()
    {
        var schema = new Option<string>("--schema") { Description = "A schema file to check in place of the host's schema; without --project or --assembly no host is built.", HelpName = "file" };
        var command = new Command("validate", "Builds the host's schema, or a schema file, and prints every error; exits with 1 when there is one.") { schema };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) =>
        {
            var file = result.GetValue(schema) is { } given ? Path.GetFullPath(given) : null;
            if (file is null || NamesHost(result, project))
            {
                return HandOverAsync(result, project, ["schema", "validate", .. file is null ? Array.Empty<string>() : ["--schema", file]], cancellationToken);
            }

            return GuardAsync(async () =>
            {
                var fieldTypes = new UncheckedFieldTypes();
                SchemaModel model;
                try
                {
                    model = ReadSchemaFile(file, fieldTypes);
                }
                catch (SchemaValidationException exception)
                {
                    var exitCode = await SchemaValidateCommand.ReportAsync(output, error, file, exception.Errors);
                    await NoteUncheckedAsync(fieldTypes);
                    return exitCode;
                }

                await output.WriteLineAsync(SchemaValidateCommand.Valid(file, model));
                await NoteUncheckedAsync(fieldTypes);
                return ExitCodes.Success;
            });
        });
        return command;
    }

    // From the side that is deployed to the new one: from the instance of --url to the host's schema or a schema file, both
    // read here; from the applied history or a schema file to the host's schema, or from the applied history to a schema
    // file, inside the host.
    private Command Diff()
    {
        var applied = new Option<bool>("--applied") { Description = "Compares from the schema in the newest row of the schema history of the host's database." };
        var schema = new Option<string>("--schema") { Description = "A schema file: what is compared from, or with --url or --applied, what is compared to in place of the host's schema.", HelpName = "file" };
        var json = new Option<bool>("--json") { Description = "Writes the report as one line of JSON." };
        var command = new Command("diff", "Compares a running instance, the applied schema history or a schema file with the host's schema, or with a schema file; exits with 1 when they differ.")
        {
            applied, schema, json,
        };

        var project = ProjectOptions.AddTo(command);
        var instance = InstanceOptions.AddTo(command);
        command.Validators.Add(result =>
        {
            if (result.GetValue(instance.Url) is not null && result.GetValue(applied))
            {
                result.AddError("--url and --applied both name the schema compared from; give one.");
            }
            else if (result.GetValue(instance.Url) is null && !result.GetValue(applied) && result.GetValue(schema) is null)
            {
                result.AddError("Give --url, --applied or --schema to compare the host's schema with.");
            }
        });

        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var file = result.GetValue(schema) is { } given ? Path.GetFullPath(given) : null;
            if (instance.Resolve(result) is not { } target)
            {
                return await HandOverAsync(
                    result,
                    project,
                    ["schema", "diff", .. Flag(result, applied), .. file is null ? Array.Empty<string>() : ["--schema", file], .. Flag(result, json)],
                    cancellationToken);
            }

            var from = Encoding.UTF8.GetString(await InstanceSnapshotAsync(target, cancellationToken));
            var (to, toLabel) = file is null
                ? (await HostSnapshotAsync(result, project, cancellationToken), "the host's schema")
                : (SchemaSnapshot.Of(DiffSchemaFile(file)).Json, file);

            SchemaDiffReport report;
            try
            {
                report = SchemaSnapshotDiff.Report(from, to);
            }
            catch (JsonException)
            {
                throw new CliException(CliErrorCodes.InstanceUnreachable, $"{target} answered schema/snapshot with something that is not a schema snapshot.");
            }

            return await SchemaSources.WriteDiffAsync(report, result.GetValue(json), $"the instance at {target}", toLabel, output, error);
        }));
        return command;
    }

    private Command Doctor()
    {
        var json = new Option<bool>("--json") { Description = "Writes the report as one line of JSON, in the shape of GET /system/diagnostics." };
        var command = new Command("doctor", "Prints the startup report without starting the host, or with --url an instance's diagnostics; exits with 1 when the host would not start or a migration is pending.") { json };
        var project = ProjectOptions.AddTo(command);
        var instance = InstanceOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            if (instance.Resolve(result) is not { } target)
            {
                return await HandOverAsync(result, project, ["doctor", .. Flag(result, json)], cancellationToken);
            }

            using var http = _connect(target);
            var body = await new InstanceClient(target, http).GetAsync("system/diagnostics", "system.read", cancellationToken);
            StartupReport? report;
            try
            {
                report = JsonSerializer.Deserialize<StartupReport>(body, StartupReportJson.Options);
            }
            catch (JsonException)
            {
                report = null;
            }

            if (report is null)
            {
                throw new CliException(CliErrorCodes.InstanceUnreachable, $"{target} answered system/diagnostics with something that is not a startup report.");
            }

            // The JSON as the instance sent it, so that what a newer instance adds is kept. An instance that answers has
            // started, so only what is pending and the versions are left to judge.
            if (result.GetValue(json))
            {
                await SchemaSources.WriteBytesAsync(output, body, cancellationToken);
                await output.WriteLineAsync();
            }
            else
            {
                await DoctorReport.WriteTextAsync(report, output);
            }

            return await DoctorReport.JudgeAsync(report, [], error);
        }));
        return command;
    }

    private async Task<byte[]> InstanceSnapshotAsync(Instance target, CancellationToken cancellationToken)
    {
        using var http = _connect(target);
        return await new InstanceClient(target, http).GetAsync("schema/snapshot", "schema.read", cancellationToken);
    }

    // The host writes its snapshot to a file of the tool's, which keeps it apart from anything else the host prints.
    private async Task<string> HostSnapshotAsync(ParseResult result, ProjectOptions project, CancellationToken cancellationToken)
    {
        var file = Path.Combine(Path.GetTempPath(), $"fluentcms-schema-{Guid.NewGuid():N}.json");
        try
        {
            using var printed = new StringWriter();
            var exitCode = await launcher.RunAsync(project.Resolve(result, error), project.EnvironmentOf(result), ["schema", "snapshot", "--output", file], printed, error, cancellationToken);
            if (exitCode != ExitCodes.Success || !File.Exists(file))
            {
                throw new CliException(CliErrorCodes.HostSnapshotFailed, $"the host did not write its schema snapshot (exit code {exitCode}), so there is nothing to compare the instance with.");
            }

            return await File.ReadAllTextAsync(file, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A file left in the temporary folder must not hide what failed.
            }
        }
    }

    private static SchemaModel ReadSchemaFile(string file, UncheckedFieldTypes fieldTypes)
    {
        try
        {
            return SchemaFiles.Read(file, fieldTypes);
        }
        catch (Exception exception) when (SchemaFiles.IsUnreadable(exception))
        {
            throw new CliException(CommandErrorCodes.SchemaFileUnreadable, $"{file} could not be read: {exception.Message}");
        }
    }

    // A file with errors has nothing to compare.
    private static SchemaModel DiffSchemaFile(string file)
    {
        try
        {
            return ReadSchemaFile(file, new UncheckedFieldTypes());
        }
        catch (SchemaValidationException exception)
        {
            throw new CliException(
                CommandErrorCodes.SchemaInvalid,
                $"{file} has {SchemaSources.Errors(exception.Errors.Count)}:{Environment.NewLine}{string.Join(Environment.NewLine, exception.Errors.Select(item => $"  {item}"))}");
        }
    }

    private async Task NoteUncheckedAsync(UncheckedFieldTypes fieldTypes)
    {
        foreach (var name in fieldTypes.Unchecked)
        {
            await error.WriteLineAsync($"note: field type '{name}' is not one of core's, so its fields were checked only as fields; give --project or --assembly to check them with the host's field types.");
        }
    }

    private static async Task WriteFileAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(path, content, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CliException(CommandErrorCodes.OutputNotWritten, $"{path} could not be written: {exception.Message}");
        }
    }

    private static bool NamesHost(ParseResult result, ProjectOptions project) =>
        result.GetValue(project.Project) is not null || result.GetValue(project.Assembly) is not null;

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
