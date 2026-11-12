using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Providers;
using FluentCMS.Seeding;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Commands;

// The host that the commands of a class share: no queue has workers, so a test runs a job itself as a worker would, and
// drains the outbox itself when it reads the activity log.
public sealed class AdminCommandFixture : IAsyncDisposable
{
    public const string Origin = "https://cms.example.com";

    public AdminCommandFixture()
    {
        Host = new FluentCmsTestHost()
            .WithServices(services =>
            {
                services.AddSingleton<IEmailSender>(Emails);
                services.AddSingleton(Probe);
            })
            .Configure(cms =>
            {
                cms.PublicOrigin = Origin;
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                cms.Jobs.AddJob<ProbeJob, ProbePayload>(ProbeJob.Type);
                cms.Seeding.Add<ProbeSeeder>();
                cms.Seeding.RunOnStart = false;
            });
    }

    public FluentCmsTestHost Host { get; }

    internal HostedPagesTests.CapturedEmails Emails { get; } = new();

    internal JobProbe Probe { get; } = new();

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

public sealed class AdminCommandTests(AdminCommandFixture fixture) : IClassFixture<AdminCommandFixture>
{
    private const string Password = "Correct-Horse-Battery-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FluentCmsTestHost Host => fixture.Host;

    [Fact]
    public async Task ApiKeyCreatePrintsAKeyThatAuthenticatesAndRevokeEndsIt()
    {
        var created = await RunAsync("apikey", "create", "--name", "deploy", "--class", "management", "--role", BuiltInRoles.SuperAdminName);

        created.ExitCode.ShouldBe(0, created.Error);
        var key = created.Output.Trim();
        key.ShouldStartWith("fcms_mk_");
        created.Output.ShouldBe(key + Environment.NewLine);
        created.Error.ShouldContain("shown only once");
        created.Error.ShouldNotContain(key);

        using var client = WithKey(key);
        (await client.GetAsync("/api/management/v1/me", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = await RunAsync("apikey", "list", "--json");
        listed.ExitCode.ShouldBe(0, listed.Error);
        listed.Output.Trim().ShouldNotContain(Environment.NewLine);
        listed.Output.ShouldNotContain(key);
        var id = Items(listed.Output).Single(item => item.GetProperty("name").GetString() == "deploy").GetProperty("id").GetString()!;

        var revoked = await RunAsync("apikey", "revoke", id);
        revoked.ExitCode.ShouldBe(0, revoked.Error);
        revoked.Output.ShouldContain($"Revoked the API key {id}");
        (await client.GetAsync("/api/management/v1/me", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var table = await RunAsync("apikey", "list");
        table.Output.ShouldStartWith("ID");
        table.Output.Split(Environment.NewLine).Single(line => line.StartsWith(id, StringComparison.Ordinal)).ShouldContain("revoked");
    }

    [Fact]
    public async Task ApiKeyCreateBindsTheSiteTypesAndExpiryAndAnswersWithTheApisShapeInJson()
    {
        var created = await RunAsync("apikey", "create", "--name", "reader", "--class", "delivery", "--role", BuiltInRoles.PublicName, "--site", "default", "--expires", "2099-01-01", "--json");

        created.ExitCode.ShouldBe(0, created.Error);
        using var json = JsonDocument.Parse(created.Output);
        json.RootElement.GetProperty("class").GetString().ShouldBe("delivery");
        json.RootElement.GetProperty("secret").GetString()!.ShouldStartWith("fcms_dk_");
        json.RootElement.GetProperty("siteIds").GetArrayLength().ShouldBe(1);
        json.RootElement.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("apikey", "create", "--name", "x", "--class", "admin", "--role", "Editor")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Nobody")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Editor", "--site", "nowhere")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Editor", "--expires", "soon")]
    [InlineData("apikey", "revoke", "not-an-id")]
    [InlineData("jobs", "list", "--status", "lost")]
    [InlineData("jobs", "run", "no.such.job")]
    [InlineData("seed", "--name", "NoSuchSeeder")]
    public async Task AValueTheHostDoesNotKnowIsBadUsage(params string[] args)
    {
        var result = await RunAsync(args);

        result.ExitCode.ShouldBe(2, result.Output);
        result.Error.ShouldStartWith("FCMS33");
    }

    [Fact]
    public async Task UserCreateMakesASuperAdminWhoSignsInThroughTheHostedPages()
    {
        var created = await RunAsync(new TextCommandInput(new StringReader(Password + "\n"), terminal: false), "user", "create", "--email", "first@example.com", "--role", BuiltInRoles.SuperAdminName, "--password-stdin");

        created.ExitCode.ShouldBe(0, created.Error);
        created.Output.ShouldContain("Created the user first@example.com");
        (created.Output + created.Error).ShouldNotContain(Password);

        using var browser = new HostedPagesBrowser(Host);
        using var signedIn = await browser.SubmitAsync("/account/login", new Dictionary<string, string> { ["Input.UserName"] = "first@example.com", ["Input.Password"] = Password });
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();

        var row = (await ActivityAsync("user.created")).Single(item => item.TargetLabel == "first@example.com");
        row.Origin.ShouldBe(Origin.Cli);
        row.ActorType.ShouldBe(PrincipalKind.System);
        (await RolesOfAsync("first@example.com")).ShouldBe([BuiltInRoles.SuperAdmin]);
    }

    [Fact]
    public async Task AtATerminalThePasswordIsAskedForTwiceOnStandardError()
    {
        var created = await RunAsync(new TextCommandInput(new StringReader($"{Password}\n{Password}\n"), terminal: true), "user", "create", "--email", "prompted@example.com", "--role", BuiltInRoles.EditorName);

        created.ExitCode.ShouldBe(0, created.Error);
        created.Error.ShouldContain("Password: ");
        created.Error.ShouldContain("Repeat the password: ");
        created.Output.ShouldNotContain("Password");

        var differ = await RunAsync(new TextCommandInput(new StringReader($"{Password}\nother\n"), terminal: true), "user", "create", "--email", "typo@example.com", "--role", BuiltInRoles.EditorName);
        differ.ExitCode.ShouldBe(1);
        differ.Error.ShouldContain($"{Environment.NewLine}FCMS3352: the two passwords differ");
    }

    [Fact]
    public async Task APasswordIsNeverReadFromAnArgument()
    {
        var argument = await RunAsync("user", "create", "--email", "argument@example.com", "--role", BuiltInRoles.EditorName, "--password", Password);
        argument.ExitCode.ShouldBe(2);
        argument.Error.ShouldStartWith("FCMS3300: --password is not an option of 'fluentcms user create'");
        argument.Error.ShouldNotContain(Password);

        var redirected = await RunAsync("user", "create", "--email", "argument@example.com", "--role", BuiltInRoles.EditorName);
        redirected.ExitCode.ShouldBe(2);
        redirected.Error.ShouldStartWith("FCMS3352: standard input is not a terminal");

        var empty = await RunAsync(new TextCommandInput(new StringReader(""), terminal: false), "user", "create", "--email", "argument@example.com", "--role", BuiltInRoles.EditorName, "--password-stdin");
        empty.ExitCode.ShouldBe(1);
        (await RolesOfAsync("argument@example.com")).ShouldBeNull();
    }

    [Fact]
    public async Task InviteResetPasswordAndAddRoleDoWhatTheirEndpointsDoWithTheCliOrigin()
    {
        var invited = await RunAsync("user", "invite", "--email", "invited@example.com", "--role", BuiltInRoles.EditorName);
        invited.ExitCode.ShouldBe(0, invited.Error);
        fixture.Emails.LinkTo("invited@example.com").AbsoluteUri.ShouldStartWith($"{AdminCommandFixture.Origin}/account/accept-invitation?token=");

        await RunAsync(new TextCommandInput(new StringReader(Password + "\n"), terminal: false), "user", "create", "--email", "editor@example.com", "--role", BuiltInRoles.EditorName, "--password-stdin");

        var mailed = await RunAsync("user", "reset-password", "--email", "editor@example.com");
        mailed.ExitCode.ShouldBe(0, mailed.Error);
        fixture.Emails.LinkTo("editor@example.com").AbsoluteUri.ShouldStartWith($"{AdminCommandFixture.Origin}/account/reset-password?");

        var added = await RunAsync("user", "add-role", "--email", "editor@example.com", "--role", BuiltInRoles.AuthorName);
        added.ExitCode.ShouldBe(0, added.Error);
        (await RolesOfAsync("editor@example.com"))!.ShouldBe([BuiltInRoles.Editor, BuiltInRoles.Author], ignoreOrder: true);
        var row = (await ActivityAsync("user.role_changed")).Last(item => item.TargetLabel == "editor@example.com");
        row.Origin.ShouldBe(Origin.Cli);

        var again = await RunAsync("user", "add-role", "--email", "editor@example.com", "--role", BuiltInRoles.AuthorName);
        again.ExitCode.ShouldBe(1);
        again.Error.ShouldStartWith("FCMS");

        var unknown = await RunAsync("user", "add-role", "--email", "nobody@example.com", "--role", BuiltInRoles.AuthorName);
        unknown.ExitCode.ShouldBe(1);
        unknown.Error.ShouldStartWith("FCMS3353");
    }

    [Fact]
    public async Task ResetPasswordWithStandardInputSetsThePassword()
    {
        await RunAsync(new TextCommandInput(new StringReader(Password + "\n"), terminal: false), "user", "create", "--email", "locked@example.com", "--role", BuiltInRoles.EditorName, "--password-stdin");
        const string NewPassword = "A-New-Longer-Passphrase-7";

        var reset = await RunAsync(new TextCommandInput(new StringReader(NewPassword + "\n"), terminal: false), "user", "reset-password", "--email", "locked@example.com", "--password-stdin");

        reset.ExitCode.ShouldBe(0, reset.Error);
        using var browser = new HostedPagesBrowser(Host);
        using var signedIn = await browser.SubmitAsync("/account/login", new Dictionary<string, string> { ["Input.UserName"] = "locked@example.com", ["Input.Password"] = NewPassword });
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
    }

    [Fact]
    public async Task JobsRunQueuesARunThatAWorkerPicksUpAndInlineRunsItHere()
    {
        await AddRecurringAsync("test.probe", new ProbePayload("queued"));

        var queued = await RunAsync("jobs", "run", "test.probe");
        queued.ExitCode.ShouldBe(0, queued.Error);
        var id = Guid.Parse(queued.Output.Split(' ')[5]);
        (await JobAsync(id)).Status.ShouldBe(JobStatus.Pending);
        (await JobAsync(id)).Origin.ShouldBe(Origin.Cli);
        fixture.Probe.Ran.ShouldNotContain("queued");

        // What a worker of a running instance does with the queue.
        var executor = Host.Services.GetRequiredService<JobExecutor>();
        while ((await JobAsync(id)).Status == JobStatus.Pending && await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "worker", Token))
        {
        }

        (await JobAsync(id)).Status.ShouldBe(JobStatus.Succeeded);
        fixture.Probe.Ran.ShouldContain("queued");

        await AddRecurringAsync("test.probe.inline", new ProbePayload("inline"));
        var inline = await RunAsync("jobs", "run", "test.probe.inline", "--inline");
        inline.ExitCode.ShouldBe(0, inline.Error);
        inline.Output.ShouldContain("it succeeded");
        fixture.Probe.Ran.ShouldContain("inline");
    }

    [Fact]
    public async Task AnInlineRunThatFailsIsDeadLetteredAndRetryPutsItBack()
    {
        await AddRecurringAsync("test.probe.failing", new ProbePayload("fail"));

        var failed = await RunAsync("jobs", "run", "test.probe.failing", "--inline");

        failed.ExitCode.ShouldBe(1);
        failed.Error.ShouldStartWith("FCMS3356");
        var id = Guid.Parse(failed.Error.Split(' ')[3]);
        (await JobAsync(id)).Status.ShouldBe(JobStatus.DeadLettered);

        var listed = await RunAsync("jobs", "list", "--status", "dead_lettered", "--json");
        listed.ExitCode.ShouldBe(0, listed.Error);
        Items(listed.Output).Select(item => item.GetProperty("id").GetGuid()).ShouldContain(id);

        var retried = await RunAsync("jobs", "retry", id.ToString());
        retried.ExitCode.ShouldBe(0, retried.Error);
        (await JobAsync(id)).Status.ShouldBe(JobStatus.Pending);

        var notDead = await RunAsync("jobs", "retry", id.ToString());
        notDead.ExitCode.ShouldBe(1);
        notDead.Error.ShouldStartWith("FCMS3133");
    }

    [Fact]
    public async Task SeedRunsTheSeedersThatAreNotAppliedAndASecondRunDoesNothing()
    {
        var first = await RunAsync("seed");
        first.ExitCode.ShouldBe(0, first.Error);
        first.Output.ShouldContain($"Applied {ProbeSeeder.SeederName} at version 1.");
        fixture.Probe.Seeded.ShouldBe(1);

        var second = await RunAsync("seed", "--name", ProbeSeeder.SeederName);
        second.ExitCode.ShouldBe(0, second.Error);
        second.Output.ShouldContain("Nothing to seed");
        fixture.Probe.Seeded.ShouldBe(1);
    }

    private Task<TestCommandResult> RunAsync(params string[] args) => RunAsync(CommandInput.None, args);

    private async Task<TestCommandResult> RunAsync(CommandInput input, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(Host.Services, [CommandRunner.Prefix, .. args], input, output, error, CommandRegistry.Commands, Token);
        return new TestCommandResult(exitCode!.Value, output.ToString(), error.ToString());
    }

    private HttpClient WithKey(string key)
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        return client;
    }

    private async Task AddRecurringAsync(string name, ProbePayload payload) =>
        await Host.Services.GetRequiredService<IJobScheduler>().AddRecurringAsync<ProbeJob, ProbePayload>(name, "0 0 1 1 *", payload, queue: null, Token);

    private async Task<Job> JobAsync(Guid id)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking().SingleAsync(job => job.Id == id, Token);
    }

    private async Task<List<Guid>?> RolesOfAsync(string email)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var user = await context.Set<CmsUser>().AsNoTracking().FirstOrDefaultAsync(row => row.Email == email, Token);
        return user is null ? null : await context.Set<CmsUserRole>().Where(row => row.UserId == user.Id).Select(row => row.RoleId).ToListAsync(Token);
    }

    private async Task<List<ActivityRow>> ActivityAsync(string action)
    {
        while (await Host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        await using var scope = Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
            .Where(row => row.Action == action)
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(Token);
    }

    private static List<JsonElement> Items(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone())];
    }
}

internal sealed class JobProbe
{
    private int _seeded;

    public List<string> Ran { get; } = [];

    public int Seeded => Volatile.Read(ref _seeded);

    public void Seed() => Interlocked.Increment(ref _seeded);
}

public sealed record ProbePayload(string Label);

internal sealed class ProbeJob(JobProbe probe) : IJob<ProbePayload>
{
    public const string Type = "test.probe";

    public Task ExecuteAsync(ProbePayload payload, JobContext context)
    {
        if (payload.Label == "fail")
        {
            throw new InvalidOperationException("The probe was told to fail.");
        }

        lock (probe.Ran)
        {
            probe.Ran.Add(payload.Label);
        }

        return Task.CompletedTask;
    }
}

internal sealed class ProbeSeeder(JobProbe probe) : ISeeder
{
    public const string SeederName = "probe";

    public string Name => SeederName;

    public Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default)
    {
        probe.Seed();
        return Task.CompletedTask;
    }
}
