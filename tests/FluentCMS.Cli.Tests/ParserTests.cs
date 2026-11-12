using System.CommandLine;
using System.Text.RegularExpressions;
using FluentCMS.Core.Commands;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

public sealed partial class ParserTests : IDisposable
{
    private static readonly string[] ProjectOptions = ["--project", "--assembly", "--no-build", "--environment"];

    // The tool reaches an instance itself, so these are never handed on.
    private static readonly string[] InstanceOptionNames = ["--url", "--token"];

    private readonly ToolRun _tool = new();
    private readonly RecordingLauncher _launcher = new();

    public void Dispose() => _tool.Dispose();

    [Theory]
    [InlineData]
    [InlineData("migrations")]
    [InlineData("frobnicate")]
    [InlineData("migrate", "--unknown")]
    [InlineData("migrate", "--project")]
    [InlineData("migrations", "add")]
    [InlineData("migrations", "add", "One", "Two")]
    [InlineData("migrations", "script", "--from", "0")]
    [InlineData("migrations", "script", "--to", "20260101000000_Initial")]
    [InlineData("migrations", "remove", "--force", "yes")]
    [InlineData("schema", "diff")]
    [InlineData("schema", "diff", "--url", "https://cms.example.com", "--applied")]
    [InlineData("schema", "snapshot", "--output")]
    [InlineData("apikey", "create", "--name", "x", "--class", "admin", "--role", "Editor")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Editor", "--expires", "soon")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Editor", "--types", ",")]
    [InlineData("apikey", "list", "--url", "https://cms.example.com", "--no-build")]
    [InlineData("apikey", "revoke", "not-an-id")]
    [InlineData("user", "create", "--email", "a@example.com", "--role", "Editor", "--password", "secret")]
    [InlineData("user", "create", "--email", "a@example.com", "--role", "Editor", "--url", "https://cms.example.com")]
    [InlineData("user", "reset-password", "--email", "a@example.com", "--url", "https://cms.example.com")]
    [InlineData("jobs", "list", "--status", "lost")]
    [InlineData("jobs", "run")]
    [InlineData("jobs", "run", "x", "--url", "https://cms.example.com")]
    [InlineData("seed", "--url", "https://cms.example.com")]
    [InlineData("mcp")]
    [InlineData("mcp", "--project", ".")]
    [InlineData("schema", "apply", "--output", "schema.sql")]
    [InlineData("schema", "apply", "--script", "--dry-run")]
    [InlineData("export")]
    [InlineData("import")]
    [InlineData("import", "package.zip", "--missing-locales", "maybe")]
    [InlineData("backup")]
    [InlineData("restore")]
    [InlineData("restore", "one.zip", "two.zip")]
    [InlineData("generate")]
    [InlineData("generate", "csharp", "--output", "out")]
    [InlineData("generate", "typescript")]
    [InlineData("generate", "source")]
    [InlineData("openapi", "document")]
    public async Task BadUsagePrintsTheHelpAndExitsWithTwo(params string[] args)
    {
        (await RunAsync(args)).ShouldBe(2);

        _tool.Error.ToString().ShouldContain("Usage:");
        _tool.Error.ToString().ShouldContain("fluentcms");
        _launcher.Arguments.ShouldBeNull();
    }

    [Fact]
    public async Task HelpAndVersionExitWithZero()
    {
        (await RunAsync("--help")).ShouldBe(0);
        _tool.Output.ToString().ShouldContain("migrate");
        _tool.Output.ToString().ShouldContain("migrations");

        (await RunAsync("migrations", "script", "--help")).ShouldBe(0);
        _tool.Output.ToString().ShouldContain("--idempotent");

        (await RunAsync("--version")).ShouldBe(0);
        _tool.Output.ToString().Trim().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(new[] { "migrate" }, new[] { "migrate" })]
    [InlineData(new[] { "migrate", "--check", "--no-build", "--environment", "Production" }, new[] { "migrate", "--check" })]
    [InlineData(new[] { "migrations", "list", "--json" }, new[] { "migrations", "list", "--json" })]
    [InlineData(new[] { "migrations", "script", "--idempotent", "--stream", "content", "--to", "X" }, new[] { "migrations", "script", "--stream", "content", "--to", "X", "--idempotent" })]
    [InlineData(new[] { "schema", "snapshot" }, new[] { "schema", "snapshot" })]
    [InlineData(new[] { "schema", "validate" }, new[] { "schema", "validate" })]
    [InlineData(new[] { "schema", "diff", "--json", "--applied" }, new[] { "schema", "diff", "--applied", "--json" })]
    [InlineData(new[] { "schema", "apply", "--allow-destructive", "--dry-run" }, new[] { "schema", "apply", "--dry-run", "--allow-destructive" })]
    [InlineData(new[] { "doctor", "--json", "--environment", "Production" }, new[] { "doctor", "--json" })]
    [InlineData(new[] { "apikey", "create", "--json", "--role", "Super Admin", "--class", "management", "--name", "ci", "--no-build" }, new[] { "apikey", "create", "--name", "ci", "--class", "management", "--role", "Super Admin", "--json" })]
    [InlineData(new[] { "apikey", "revoke", "0190A3C4-5E6F-7A8B-9C0D-1E2F3A4B5C6D" }, new[] { "apikey", "revoke", "0190a3c4-5e6f-7a8b-9c0d-1e2f3a4b5c6d" })]
    [InlineData(new[] { "user", "create", "--password-stdin", "--email", "a@example.com", "--role", "Super Admin" }, new[] { "user", "create", "--email", "a@example.com", "--role", "Super Admin", "--password-stdin" })]
    [InlineData(new[] { "user", "add-role", "--email", "a@example.com", "--role", "Editor", "--site", "docs" }, new[] { "user", "add-role", "--email", "a@example.com", "--role", "Editor", "--site", "docs" })]
    [InlineData(new[] { "jobs", "run", "--inline", "fluentcms.jobs.retention" }, new[] { "jobs", "run", "fluentcms.jobs.retention", "--inline" })]
    [InlineData(new[] { "jobs", "list", "--status", "dead_lettered" }, new[] { "jobs", "list", "--status", "dead_lettered" })]
    [InlineData(new[] { "seed", "--name", "BlogSeeder" }, new[] { "seed", "--name", "BlogSeeder" })]
    public async Task TheCommandIsHandedOnWithoutTheToolsOwnOptions(string[] args, string[] handedOn)
    {
        (await RunAsync([.. args, "--assembly", ToolRun.HostAssembly])).ShouldBe(RecordingLauncher.ExitCode);

        _launcher.Arguments.ShouldBe(handedOn);
        _launcher.Output.ShouldBeSameAs(_tool.Output);
    }

    [Fact]
    public async Task TheEnvironmentAndTheProjectDirectoryGoToTheHost()
    {
        await RunAsync("migrate", "--environment", "Staging", "--assembly", ToolRun.HostAssembly, "--project", _tool.Root);

        _launcher.Environment.ShouldBe("Staging");
        _launcher.Host!.ProjectDirectory.ShouldBe(_tool.Root);
        _launcher.Host.AssemblyPath.ShouldBe(ToolRun.HostAssembly);
    }

    [Fact]
    public async Task TheOutputOfAScriptIsTheCallersPath()
    {
        await RunAsync("migrations", "script", "--output", "out.sql", "--assembly", ToolRun.HostAssembly);

        _launcher.Arguments.ShouldBe(["migrations", "script", "--output", Path.GetFullPath("out.sql")]);
    }

    [Fact]
    public async Task ThePathsOfTheSchemaCommandsAreTheCallers()
    {
        await RunAsync("schema", "snapshot", "--output", "schema.json", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["schema", "snapshot", "--output", Path.GetFullPath("schema.json")]);

        await RunAsync("schema", "diff", "--schema", "deployed.json", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["schema", "diff", "--schema", Path.GetFullPath("deployed.json")]);

        await RunAsync("schema", "apply", "--script", "--output", "schema.sql", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["schema", "apply", "--script", "--output", Path.GetFullPath("schema.sql")]);

        // A schema file goes to the host only when a host is named, which checks it with its own field types.
        await RunAsync("schema", "validate", "--schema", "schema.json", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["schema", "validate", "--schema", Path.GetFullPath("schema.json")]);
    }

    [Fact]
    public async Task ExportImportBackupAndRestoreHandOnTheirOptionsWithTheCallersPaths()
    {
        await RunAsync("export", "--output", "out.zip", "--site", "blog", "--types", "article,tag", "--published-only", "--roles", "--media-files", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["export", "--output", Path.GetFullPath("out.zip"), "--site", "blog", "--types", "article,tag", "--published-only", "--roles", "--media-files"]);

        await RunAsync("import", "in.zip", "--json", "--dry-run", "--missing-locales", "skip", "--overwrite", "--webhooks", "--roles", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["import", Path.GetFullPath("in.zip"), "--dry-run", "--overwrite", "--webhooks", "--missing-locales", "skip", "--roles", "--json"]);

        await RunAsync("backup", "--output", "backup.zip", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["backup", "--output", Path.GetFullPath("backup.zip")]);

        await RunAsync("restore", "backup.zip", "--force", "--assembly", ToolRun.HostAssembly);
        _launcher.Arguments.ShouldBe(["restore", Path.GetFullPath("backup.zip"), "--force"]);
    }

    [Fact]
    public async Task AKeyWithoutAnAddressIsBadUsageAndNothingRuns()
    {
        (await RunAsync("export", "--output", "out.zip", "--token", "secret-key", "--assembly", ToolRun.HostAssembly)).ShouldBe(2);

        _tool.Error.ToString().ShouldStartWith("FCMS3316: ");
        _tool.Error.ToString().ShouldNotContain("secret-key");
        _launcher.Arguments.ShouldBeNull();
    }

    // The host reads what the tool hands on with its own parser, so the two must name the same commands and options. A
    // hidden command is the tool's way to read from the host, and the tool has no command of its name.
    [Fact]
    public void EveryCommandOfTheHostIsInTheToolWithTheSameOptions()
    {
        var tree = CommandLine.Tree();
        foreach (var command in CommandRegistry.Commands.Where(command => !command.Hidden))
        {
            var tool = command.Name.Split(' ').Aggregate(tree, (parent, word) => parent.Subcommands.Single(child => child.Name == word));

            tool.Options.Select(option => option.Name).Where(name => !ProjectOptions.Contains(name) && !InstanceOptionNames.Contains(name) && name != "--help")
                .ShouldBe(command.Options.Select(option => option.Name), ignoreOrder: true, command.Name);
            tool.Arguments.Count.ShouldBe(command.Arguments.Count, command.Name);
        }
    }

    // `mcp` reaches a running instance only.
    [Fact]
    public void AHiddenCommandIsNotInTheToolAndTheGenerateCommandsAskTheHostWithIt()
    {
        var tree = CommandLine.Tree();
        foreach (var command in CommandRegistry.Commands.Where(command => command.Hidden))
        {
            tree.Parse(command.Name.Split(' ')).Errors.ShouldNotBeEmpty(command.Name);
        }

        CommandRegistry.Usage.ShouldNotContain("openapi document");
    }

    [Theory]
    [InlineData("typescript")]
    [InlineData("openapi")]
    public async Task GenerateInProjectModeAsksTheHostForItsSource(string language)
    {
        (await RunAsync("generate", language, "--output", Path.Combine(_tool.Root, "out"), "--assembly", ToolRun.HostAssembly, "--environment", "Staging")).ShouldBe(1);

        _launcher.Arguments!.Take(3).ShouldBe(language == "openapi" ? ["openapi", "document", "--output"] : ["schema", "snapshot", "--output"]);
        _launcher.Environment.ShouldBe("Staging");
        _tool.Error.ToString().ShouldStartWith("FCMS3378: ");
    }

    [Fact]
    public void EveryProjectCommandTakesTheProjectOptions()
    {
        foreach (var command in Leaves(CommandLine.Tree()).Where(command => command.Name != "mcp"))
        {
            ProjectOptions.ShouldBeSubsetOf(command.Options.Select(option => option.Name), command.Name);
        }
    }

    // Every "fluentcms ..." that a message in the source tells someone to run is a command of the tool.
    [Fact]
    public void EveryMessageThatNamesACommandNamesOneThatExists()
    {
        var source = Path.Combine(RepositoryRoot(), "src");
        var named = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => NamedCommand().Matches(File.ReadAllText(path)).Select(match => (Words: match.Groups["words"].Value.Trim(), File: Path.GetFileName(path))))
            .ToList();

        named.ShouldContain(item => item.Words == "migrate");
        named.ShouldContain(item => item.Words.StartsWith("migrations add", StringComparison.Ordinal));

        var tree = CommandLine.Tree();
        foreach (var (words, file) in named)
        {
            var result = tree.Parse(words.Split(' '));
            result.UnmatchedTokens.ShouldBeEmpty($"{file}: 'fluentcms {words}'");
            result.CommandResult.Command.Action.ShouldNotBeNull($"{file}: 'fluentcms {words}'");
        }
    }

    [Fact]
    public void AnInstanceIsAnAddressAndAKeyFromTheOptionOrTheEnvironment()
    {
        var command = new Command("test");
        var options = InstanceOptions.AddTo(command);
        Func<string, string?> environment = name => name == InstanceOptions.TokenVariable ? "from-environment" : null;

        options.Resolve(command.Parse([]), environment).ShouldBeNull();
        options.Resolve(command.Parse(["--url", "https://cms.example.com"]), environment).ShouldBe(new Instance(new Uri("https://cms.example.com"), "from-environment"));
        options.Resolve(command.Parse(["--url", "https://cms.example.com", "--token", "given"]), environment)!.Token.ShouldBe("given");
        options.Resolve(command.Parse(["--url", "https://cms.example.com", "--token", "given"]), environment)!.ToString().ShouldNotContain("given");

        Refused(() => options.Resolve(command.Parse(["--url", "https://cms.example.com"]), _ => null)).ShouldContain($"--token or {InstanceOptions.TokenVariable}");
        Refused(() => options.Resolve(command.Parse(["--url", "cms.example.com"]), environment)).ShouldContain("http or https");
        Refused(() => options.Resolve(command.Parse(["--url", "https://user:secret@cms.example.com"]), environment)).ShouldNotContain("secret@");
        Refused(() => options.Resolve(command.Parse(["--token", "given"]), environment)).ShouldContain("--token is for --url");
    }

    private static string Refused(Action resolve)
    {
        var exception = Should.Throw<CliException>(resolve);
        exception.Code.ShouldBe("FCMS3316");
        exception.ExitCode.ShouldBe(2);
        return exception.Message;
    }

    private Task<int> RunAsync(params string[] args) => RunWithLauncherAsync(args);

    private async Task<int> RunWithLauncherAsync(string[] args)
    {
        _tool.Output.GetStringBuilder().Clear();
        _tool.Error.GetStringBuilder().Clear();
        return await CommandLine.RunAsync(args, _tool.Output, _tool.Error, _launcher, ToolRun.Token);
    }

    private static IEnumerable<Command> Leaves(Command command) =>
        command.Subcommands.Count == 0 ? [command] : command.Subcommands.SelectMany(Leaves);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    // A quoted run of the command, such as 'fluentcms migrate' or `fluentcms migrations add AddSubtitle`, without a
    // placeholder or an interpolation in it.
    [GeneratedRegex(@"['`]fluentcms (?<words>[a-z][a-z-]*(?: [A-Za-z][\w-]*)*)['`]")]
    private static partial Regex NamedCommand();

    private sealed class RecordingLauncher : IHostLauncher
    {
        public const int ExitCode = 7;

        public IReadOnlyList<string>? Arguments { get; private set; }

        public HostTarget? Host { get; private set; }

        public string? Environment { get; private set; }

        public TextWriter? Output { get; private set; }

        public Task<int> RunAsync(HostTarget host, string? environment, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            Host = host;
            Environment = environment;
            Arguments = arguments;
            Output = output;
            return Task.FromResult(ExitCode);
        }
    }
}
