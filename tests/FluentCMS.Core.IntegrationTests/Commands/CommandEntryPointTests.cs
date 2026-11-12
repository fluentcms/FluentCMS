using FluentCMS.Content;
using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Commands;

// A host's command entry point: "fluentcms <command>" runs before any hosted service has started, as the system with the
// Cli origin, and a host that never calls the entry point stops before it changes anything.
public sealed class CommandEntryPointTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-commands-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task ACommandStartsNoHostedServiceSoNothingListensRunsJobsOrSeeds()
    {
        var spy = new HostedServiceSpy();
        var seeded = false;
        await using var host = new FluentCmsTestHost()
            .UseSqlite(Path.Combine(_directory.FullName, "cms.db"))
            .WithServices(services => services.AddSingleton<IHostedService>(spy))
            .Seed(_ =>
            {
                seeded = true;
                return Task.CompletedTask;
            });

        var result = await host.RunCommandAsync("migrate");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldContain($"system{Environment.NewLine}  ");
        spy.Calls.ShouldBeEmpty();
        seeded.ShouldBeFalse();

        // The same host, started: its hosted services run, on the database the command migrated.
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeTrue();
        spy.Calls.ShouldBe(["starting", "start", "started"]);
        seeded.ShouldBeTrue();
    }

    [Fact]
    public async Task AHostWithoutTheEntryPointStopsWithTheLineToAddAndChangesNothing()
    {
        var file = Path.Combine(_directory.FullName, "cms.db");
        await using var host = new FluentCmsTestHost()
            .UseSqlite(file)
            .WithServices(services => services.AddSingleton(new ProcessArguments(["fluentcms", "migrate", "--check"])));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS3301");
        exception.Message.ShouldBe(
            "FCMS3301: the process was started with 'fluentcms migrate', but this host does not run commands; add 'if (await app.RunFluentCmsCommandAsync(args) is { } exitCode) return exitCode;' to Program.cs between Build() and Run()");
        Tables(file).ShouldBeEmpty();
    }

    // The host does not stop at the first service that fails when it starts them together, so the migrations ask too.
    [Fact]
    public async Task AHostThatStartsItsServicesTogetherStopsBeforeItMigratesToo()
    {
        var file = Path.Combine(_directory.FullName, "cms.db");
        await using var host = new FluentCmsTestHost()
            .UseSqlite(file)
            .WithServices(services =>
            {
                services.AddSingleton(new ProcessArguments(["fluentcms", "migrate"]));
                services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
            });

        var exception = Should.Throw<Exception>(() => host.Services);

        IReadOnlyList<Exception> errors = exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [exception];
        errors.ShouldAllBe(error => error is FluentCmsConfigurationException && ((FluentCmsConfigurationException)error).Code == "FCMS3301");
        Tables(file).ShouldBeEmpty();
    }

    [Fact]
    public async Task AHostStartedWithOtherArgumentsStarts()
    {
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton(new ProcessArguments(["--urls", "http://localhost:5000"])));

        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task ACommandRunsAsTheSystemWithTheCliOriginInTheDefaultSite()
    {
        await using var host = new FluentCmsTestHost();
        var probe = new OperationProbe();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandRunner.RunAsync(host.Services, ["fluentcms", "probe"], output, error, [probe], Token);

        exitCode.ShouldBe(0);
        probe.Seen!.Principal.ShouldBe(Principal.System);
        probe.Seen.Origin.ShouldBe(Origin.Cli);
        probe.Seen.Site.ShouldBe(new SiteId(DefaultSite.Id));
        host.Services.GetRequiredService<IOperationContextAccessor>().Current.ShouldBeNull();
    }

    [Fact]
    public async Task AFailureIsReportedWithItsTypeAndNoSecretAndExitsWithOne()
    {
        await using var host = new FluentCmsTestHost();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandRunner.RunAsync(host.Services, ["fluentcms", "probe"], output, error, [new OperationProbe { Throws = true }], Token);

        exitCode.ShouldBe(1);
        error.ToString().ShouldStartWith("FCMS3302: 'fluentcms probe' failed: InvalidOperationException: the database refused Password=***");
        error.ToString().ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task ArgumentsThatNameNoCommandAreLeftToTheHost()
    {
        using var host = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings()).Build();

        (await host.RunFluentCmsCommandAsync([], Token)).ShouldBeNull();
        (await host.RunFluentCmsCommandAsync(["--urls", "http://localhost:5000"], Token)).ShouldBeNull();
        (await host.RunFluentCmsCommandAsync(["migrate"], Token)).ShouldBeNull();
    }

    [Theory]
    [InlineData(new string[0], "FCMS3300: no command was given.")]
    [InlineData(new[] { "seed" }, "FCMS3300: 'seed' is not a command that this host runs.")]
    [InlineData(new[] { "migrate", "--now" }, "FCMS3300: --now is not an option of 'fluentcms migrate'.")]
    public async Task BadUsageInTheHostPrintsTheUsageAndExitsWithTwo(string[] args, string message)
    {
        await using var host = new FluentCmsTestHost();

        var result = await host.RunCommandAsync(args);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldStartWith(message);
        result.Error.ShouldContain("Usage: fluentcms ");
    }

    [Fact]
    public async Task HelpPrintsTheUsageAndExitsWithZero()
    {
        await using var host = new FluentCmsTestHost();

        var result = await host.RunCommandAsync("migrations", "script", "--help");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldStartWith("Usage: fluentcms migrations script [--stream <name>]");
    }

    [Fact]
    public async Task ACommandRunsAgainstAStartedTestHostToo()
    {
        await using var host = new FluentCmsTestHost();
        _ = host.Services;

        var result = await host.RunCommandAsync("migrations", "list");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldContain("  applied  ");
        result.Output.ShouldNotContain("  pending  ");
    }

    private static List<string> Tables(string file)
    {
        if (!File.Exists(file))
        {
            return [];
        }

        using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select name from sqlite_master where type = 'table'";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private sealed class HostedServiceSpy : IHostedLifecycleService
    {
        public List<string> Calls { get; } = [];

        public Task StartingAsync(CancellationToken cancellationToken) => Record("starting");

        public Task StartAsync(CancellationToken cancellationToken) => Record("start");

        public Task StartedAsync(CancellationToken cancellationToken) => Record("started");

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Task Record(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private sealed class OperationProbe : HostCommand
    {
        public OperationContext? Seen { get; private set; }

        public bool Throws { get; init; }

        public override string Name => "probe";

        public override string Description => "Records the operation it runs in.";

        public override Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Seen = context.Services.GetRequiredService<IOperationContextAccessor>().Current;
            return Throws ? throw new InvalidOperationException("the database refused Password=hunter2") : Task.FromResult(0);
        }
    }
}
