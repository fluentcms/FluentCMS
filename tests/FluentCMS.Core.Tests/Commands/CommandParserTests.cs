using FluentCMS.Core.Commands;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Commands;

public sealed class CommandParserTests
{
    private static readonly IReadOnlyList<HostCommand> Commands = [new Probe("jobs"), new Probe("jobs run", ["name"]), .. CommandRegistry.Commands];

    [Fact]
    public void TheLongestCommandNameWins()
    {
        var parsed = CommandParser.Parse(Commands, ["jobs", "run", "purge"]);

        parsed.Error.ShouldBeNull();
        parsed.Command!.Name.ShouldBe("jobs run");
        parsed.Arguments!.Positional.ShouldBe(["purge"]);
    }

    [Theory]
    [InlineData("--stream", "content")]
    [InlineData("--stream=content")]
    public void AnOptionTakesItsValueAfterASpaceOrAnEqualsSign(params string[] option)
    {
        var parsed = CommandParser.Parse(Commands, ["migrations", "script", .. option, "--idempotent"]);

        parsed.Error.ShouldBeNull();
        parsed.Arguments!.Value("--stream").ShouldBe("content");
        parsed.Arguments.Has("--idempotent").ShouldBeTrue();
        parsed.Arguments.Has("--output").ShouldBeFalse();
    }

    [Theory]
    [InlineData(new string[0], "no command was given")]
    [InlineData(new[] { "frobnicate" }, "'frobnicate' is not a command that this host runs")]
    [InlineData(new[] { "migrations", "add", "Initial" }, "'migrations add Initial' is not a command that this host runs")]
    [InlineData(new[] { "migrate", "--force" }, "--force is not an option of 'fluentcms migrate'")]
    [InlineData(new[] { "migrate", "--check", "--check" }, "--check is given more than once")]
    [InlineData(new[] { "migrate", "--check=yes" }, "--check takes no value")]
    [InlineData(new[] { "migrations", "script", "--stream" }, "--stream needs a value")]
    [InlineData(new[] { "migrations", "script", "--stream", "--idempotent" }, "--stream needs a value")]
    [InlineData(new[] { "migrate", "now" }, "'now' is not an argument of 'fluentcms migrate'")]
    [InlineData(new[] { "jobs", "run" }, "'fluentcms jobs run' needs <name>")]
    public void BadUsageIsNamed(string[] args, string error)
    {
        var parsed = CommandParser.Parse(Commands, args);

        parsed.Error.ShouldBe(error);
        parsed.Arguments.ShouldBeNull();
    }

    [Fact]
    public void HelpIsAskedForAnywhere()
    {
        CommandParser.Parse(Commands, ["migrate", "--help"]).ShouldSatisfyAllConditions(
            parsed => parsed.Help.ShouldBeTrue(),
            parsed => parsed.Command!.Name.ShouldBe("migrate"));
        CommandParser.Parse(Commands, ["-h"]).Help.ShouldBeTrue();
    }

    [Fact]
    public void TheUsageNamesTheArgumentsAndOptions()
    {
        new Probe("jobs run", ["name"]).Usage.ShouldStartWith("Usage: fluentcms jobs run <name>");
        new MigrationsScriptCommand().Usage.ShouldStartWith("Usage: fluentcms migrations script [--stream <name>] [--from <migration>] [--to <migration>] [--idempotent] [--output <file>]");
        CommandRegistry.Usage.ShouldContain("  migrations script  ");
    }

    private sealed class Probe(string name, IReadOnlyList<string>? arguments = null) : HostCommand
    {
        public override string Name => name;

        public override string Description => "A probe.";

        public override IReadOnlyList<string> Arguments => arguments ?? [];

        public override Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
