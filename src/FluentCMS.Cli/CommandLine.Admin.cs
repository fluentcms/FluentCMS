using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;

namespace FluentCMS.Cli;

// The commands that administer an instance (B-91). In project mode they run inside the host as the system with the Cli
// origin; with --url they call the Management API with the key of --token, as that key's principal. `user create`,
// `user reset-password`, `jobs run` and `seed` run in the host only, and `mcp` reaches a running instance only.
internal sealed partial class CommandLine
{
    private static readonly string[] KeyClasses = ["delivery", "preview", "management"];
    private static readonly string[] JobStatuses = ["pending", "running", "succeeded", "dead_lettered"];

    private Command ApiKey() => new("apikey", "API keys, in the host project or in a running instance with --url.")
    {
        ApiKeyCreate(),
        ApiKeyList(),
        ApiKeyRevoke(),
    };

    private Command User() => new("user", "Admin users: create and reset-password in the host project; invite and add-role there or with --url.")
    {
        UserCreate(),
        UserInvite(),
        UserResetPassword(),
        UserAddRole(),
    };

    private Command Jobs() => new("jobs", "Background jobs: list and retry in the host project or with --url; run in the host project.")
    {
        JobsList(),
        JobsRun(),
        JobsRetry(),
    };

    private Command ApiKeyCreate()
    {
        var name = Text("--name", "The name of the key, which lists and the activity log show.", "name", required: true);
        var keyClass = Text("--class", "delivery (published content), preview (drafts too) or management (what the role allows).", "class", required: true);
        keyClass.AcceptOnlyFromAmong(KeyClasses);
        var role = Text("--role", "The role the key acts with.", "name", required: true);
        var site = Text("--site", "The slug of the one site the key is bound to; every site when left out. The role may be the site's or instance-wide.", "slug");
        var types = Text("--types", "The content types the key is bound to, separated by commas; every type when left out.", "list");
        var expires = Text("--expires", "When the key expires, such as 2027-06-30 or 2027-06-30T12:00:00Z (UTC unless an offset is given); never when left out.", "date");
        var json = Json("Writes the Management API's answer, which holds the key as secret, as one line of JSON.");
        var command = new Command("create", "Creates an API key bound to a role and prints the key once, alone on standard output.") { name, keyClass, role, site, types, expires, json };
        command.Validators.Add(result =>
        {
            if (result.GetValue(expires) is { } value && ExpiryOf(value) is null)
            {
                result.AddError("--expires is a date such as 2027-06-30, or a time such as 2027-06-30T12:00:00Z.");
            }

            if (result.GetValue(types) is { } list && TypesOf(list).Count == 0)
            {
                result.AddError("--types names at least one content type; leave it out for every type.");
            }
        });

        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result,
            project,
            instance,
            ["apikey", "create", .. Value(result, name), .. Value(result, keyClass), .. Value(result, role), .. Value(result, site), .. Value(result, types), .. Value(result, expires), .. Flag(result, json)],
            remote => remote.ApiKeyCreateAsync(
                result.GetValue(name)!,
                result.GetValue(keyClass)!,
                result.GetValue(role)!,
                result.GetValue(site),
                result.GetValue(types) is { } list ? TypesOf(list) : null,
                result.GetValue(expires) is { } value ? ExpiryOf(value) : null,
                result.GetValue(json),
                cancellationToken),
            cancellationToken));
        return command;
    }

    private Command ApiKeyList()
    {
        var json = Json("Writes the list as one line of JSON, as the Management API answers it.");
        var command = new Command("list", "Lists the API keys, revoked and expired ones included; a key itself is never shown again.") { json };
        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result, project, instance, ["apikey", "list", .. Flag(result, json)], remote => remote.ApiKeyListAsync(result.GetValue(json), cancellationToken), cancellationToken));
        return command;
    }

    private Command ApiKeyRevoke()
    {
        var id = new Argument<Guid>("id") { Description = "The id of the key, as apikey list shows it." };
        var command = new Command("revoke", "Revokes an API key, which is refused from then on and stays in the list.") { id };
        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result, project, instance, ["apikey", "revoke", result.GetValue(id).ToString("D")], remote => remote.ApiKeyRevokeAsync(result.GetValue(id), cancellationToken), cancellationToken));
        return command;
    }

    // No option takes the password: an argument stays in the process list and the shell's history.
    private Command UserCreate()
    {
        var email = Text("--email", "The email address, which is also the user name.", "address", required: true);
        var role = Text("--role", "The role the user gets, such as \"Super Admin\".", "name", required: true);
        var site = SiteOfRole();
        var stdin = PasswordStdin("Reads the password from the first line of standard input instead of asking for it.");
        var command = new Command("create", "Creates an active admin user with a role, in the host project only; the password is asked for, or read from standard input.") { email, role, site, stdin };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(
            result, project, ["user", "create", .. Value(result, email), .. Value(result, role), .. Value(result, site), .. Flag(result, stdin)], cancellationToken));
        return command;
    }

    private Command UserInvite()
    {
        var email = Text("--email", "The email address to invite.", "address", required: true);
        var role = Text("--role", "The role the user gets on accepting.", "name", required: true);
        var site = SiteOfRole();
        var command = new Command("invite", "Sends an invitation by email; the person chooses a password on accepting it.") { email, role, site };
        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result,
            project,
            instance,
            ["user", "invite", .. Value(result, email), .. Value(result, role), .. Value(result, site)],
            remote => remote.UserInviteAsync(result.GetValue(email)!, result.GetValue(role)!, result.GetValue(site), cancellationToken),
            cancellationToken));
        return command;
    }

    // The Management API sends no reset email, so this one runs in the host project only.
    private Command UserResetPassword()
    {
        var email = Text("--email", "The email address of the user.", "address", required: true);
        var stdin = PasswordStdin("Sets the password read from the first line of standard input instead of sending the email.");
        var command = new Command("reset-password", "Sends a password reset email, or with --password-stdin sets the password; in the host project only.") { email, stdin };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(result, project, ["user", "reset-password", .. Value(result, email), .. Flag(result, stdin)], cancellationToken));
        return command;
    }

    private Command UserAddRole()
    {
        var email = Text("--email", "The email address of the user.", "address", required: true);
        var role = Text("--role", "The role to give.", "name", required: true);
        var site = SiteOfRole();
        var command = new Command("add-role", "Gives a user a role.") { email, role, site };
        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result,
            project,
            instance,
            ["user", "add-role", .. Value(result, email), .. Value(result, role), .. Value(result, site)],
            remote => remote.UserAddRoleAsync(result.GetValue(email)!, result.GetValue(role)!, result.GetValue(site), cancellationToken),
            cancellationToken));
        return command;
    }

    private Command JobsList()
    {
        var queue = Text("--queue", "Only the jobs of this queue.", "name");
        var status = Text("--status", $"Only the jobs with this status: {string.Join(", ", JobStatuses)}.", "status");
        status.AcceptOnlyFromAmong(JobStatuses);
        var json = Json("Writes the list as one line of JSON, as the Management API answers it.");
        var command = new Command("list", "Lists the newest 100 jobs, the newest first; a payload is never shown.") { queue, status, json };
        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result,
            project,
            instance,
            ["jobs", "list", .. Value(result, queue), .. Value(result, status), .. Flag(result, json)],
            remote => remote.JobsListAsync(result.GetValue(queue), result.GetValue(status), result.GetValue(json), cancellationToken),
            cancellationToken));
        return command;
    }

    private Command JobsRun()
    {
        var name = new Argument<string>("name") { Description = "The name of a recurring job, such as fluentcms.jobs.retention." };
        var inline = new Option<bool>("--inline") { Description = "Runs the job in this process with one attempt rather than queuing it, for an instance that is not running." };
        var command = new Command("run", "Queues a run of a recurring job now for the running instances, or runs it here with --inline; in the host project only.") { name, inline };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(result, project, ["jobs", "run", result.GetValue(name)!, .. Flag(result, inline)], cancellationToken));
        return command;
    }

    private Command JobsRetry()
    {
        var id = new Argument<Guid>("id") { Description = "The id of a dead-lettered job, as jobs list shows it." };
        var command = new Command("retry", "Queues a dead-lettered job again, with the runner's default number of attempts.") { id };
        var (project, instance) = BothModes(command);
        command.SetAction((result, cancellationToken) => ProjectOrInstanceAsync(
            result, project, instance, ["jobs", "retry", result.GetValue(id).ToString("D")], remote => remote.JobsRetryAsync(result.GetValue(id), cancellationToken), cancellationToken));
        return command;
    }

    private Command Seed()
    {
        var name = Text("--name", "Runs only the seeder of this name, if it is not applied at its version.", "seeder");
        var command = new Command("seed", "Runs the host's seeders that are not applied at their version; a second run does nothing. In the host project only.") { name };
        var project = ProjectOptions.AddTo(command);
        command.SetAction((result, cancellationToken) => HandOverAsync(result, project, ["seed", .. Value(result, name)], cancellationToken));
        return command;
    }

    private Command Mcp()
    {
        var command = new Command("mcp", "A proxy from standard input and output to the MCP endpoint of a running instance, for an MCP client that speaks stdio only.");
        var instance = InstanceOptions.AddTo(command);
        instance.Url.Required = true;
        command.SetAction((result, cancellationToken) => GuardAsync(async () =>
        {
            var target = instance.Resolve(result) ?? throw new CliException(CliErrorCodes.InvalidInstance, "mcp needs --url.", ExitCodes.BadUsage);
            using var client = Connect(target, Timeout.InfiniteTimeSpan);

            // On the console the protocol's UTF-8 is written and read as bytes, whatever the console's own encoding is.
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var reader = input is null ? new StreamReader(Console.OpenStandardInput(), encoding) : null;
            await using var writer = ReferenceEquals(output, Console.Out) ? new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true } : null;
            using var proxy = new McpProxy(client, input ?? reader!, writer ?? output, error);
            return await proxy.RunAsync(cancellationToken);
        }));
        return command;
    }

    // --url reaches a running instance, where the options of the project do not apply.
    private static (ProjectOptions Project, InstanceOptions Instance) BothModes(Command command)
    {
        var project = ProjectOptions.AddTo(command);
        var instance = InstanceOptions.AddTo(command);
        command.Validators.Add(result =>
        {
            Option[] projectOnly = [project.Project, project.Assembly, project.NoBuild, project.Environment];
            if (result.GetResult(instance.Url) is not null && projectOnly.Any(option => result.GetResult(option) is not null))
            {
                result.AddError("--url reaches a running instance, so --project, --assembly, --no-build and --environment do not apply.");
            }
        });
        return (project, instance);
    }

    private Task<int> ProjectOrInstanceAsync(
        ParseResult result,
        ProjectOptions project,
        InstanceOptions instance,
        IReadOnlyList<string> arguments,
        Func<RemoteCommands, Task<int>> remote,
        CancellationToken cancellationToken) =>
        GuardAsync(async () =>
        {
            if (instance.Resolve(result) is not { } target)
            {
                return await launcher.RunAsync(project.Resolve(result, error), project.EnvironmentOf(result), arguments, output, error, cancellationToken);
            }

            using var client = Connect(target);
            return await remote(new RemoteCommands(client, output, error));
        });

    // A key sent over plain http to another machine can be read on the way, which the caller is told; on this machine it is
    // a local instance, such as one in development.
    private ManagementClient Connect(Instance target, TimeSpan? timeout = null)
    {
        var client = new ManagementClient(target, _connect(target), timeout);
        if (target.Url.Scheme == Uri.UriSchemeHttp && !target.Url.IsLoopback)
        {
            error.WriteLine($"{CliErrorCodes.KeyWithoutTls}: {target} is http, so the key is sent without TLS; use https outside a trusted network.");
        }

        return client;
    }

    private static Option<string> Text(string name, string description, string valueName, bool required = false) =>
        new(name) { Description = description, HelpName = valueName, Required = required };

    private static Option<string> SiteOfRole() => Text("--site", "The slug of the site whose role is meant; an instance-wide role when left out.", "slug");

    private static Option<bool> Json(string description) => new("--json") { Description = description };

    private static Option<bool> PasswordStdin(string description) => new("--password-stdin") { Description = description };

    private static List<string> TypesOf(string list) => [.. list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    // As the host reads it: UTC unless the value names an offset.
    private static DateTimeOffset? ExpiryOf(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires) ? expires : null;
}
