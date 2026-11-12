using System.CommandLine;
using System.Text.RegularExpressions;
using FluentCMS.Core.Commands;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

public sealed partial class ParserTests : IDisposable
{
    private static readonly string[] ProjectOptions = ["--project", "--assembly", "--no-build", "--environment"];

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

    // The host reads what the tool hands on with its own parser, so the two must name the same commands and options.
    [Fact]
    public void EveryCommandOfTheHostIsInTheToolWithTheSameOptions()
    {
        var tree = CommandLine.Tree();
        foreach (var command in CommandRegistry.Commands)
        {
            var tool = command.Name.Split(' ').Aggregate(tree, (parent, word) => parent.Subcommands.Single(child => child.Name == word));

            tool.Options.Select(option => option.Name).Where(name => !ProjectOptions.Contains(name) && name != "--help")
                .ShouldBe(command.Options.Select(option => option.Name), ignoreOrder: true, command.Name);
            tool.Arguments.Count.ShouldBe(command.Arguments.Count, command.Name);
        }
    }

    [Fact]
    public void EveryProjectCommandTakesTheProjectOptions()
    {
        foreach (var command in Leaves(CommandLine.Tree()))
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
