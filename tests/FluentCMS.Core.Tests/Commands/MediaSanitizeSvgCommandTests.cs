using FluentCMS.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Commands;

// The pass is the media plugin's; the tests of the plugin run it.
public sealed class MediaSanitizeSvgCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHostWithoutTheMediaPluginFailsWithItsCode()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exception = await Should.ThrowAsync<CommandException>(() => new MediaSanitizeSvgCommand().RunAsync(new CommandContext(services, Arguments(), output, error), Token));

        exception.Code.ShouldBe(CommandErrorCodes.SvgsNotSanitized);
        exception.Message.ShouldStartWith("FCMS3309: this host has no media plugin");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public async Task TheRunOfThePluginIsHandedTheDryRunAndWhatItLeavesFailsTheCommand(bool dryRun, int left)
    {
        bool? handed = null;
        await using var services = new ServiceCollection()
            .AddKeyedSingleton<Func<bool, TextWriter, TextWriter, CancellationToken, Task<int>>>(MediaSanitizeSvgCommand.RunKey, (_, _) => (given, _, _, _) =>
            {
                handed = given;
                return Task.FromResult(left);
            })
            .BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CommandContext(services, Arguments(dryRun ? [MediaSanitizeSvgCommand.DryRun] : []), output, error);

        if (left == 0)
        {
            (await new MediaSanitizeSvgCommand().RunAsync(context, Token)).ShouldBe(CommandExitCodes.Success);
        }
        else
        {
            (await Should.ThrowAsync<CommandException>(() => new MediaSanitizeSvgCommand().RunAsync(context, Token)))
                .Message.ShouldStartWith($"FCMS3309: {left} stored SVGs are left as they are");
        }

        handed.ShouldBe(dryRun);
    }

    private static CommandArguments Arguments(params string[] flags) => new([], flags.ToDictionary(flag => flag, _ => (string?)null));
}
