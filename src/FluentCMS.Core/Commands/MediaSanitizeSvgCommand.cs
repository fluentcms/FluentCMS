using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// Sanitises the stored SVGs of the media libraries again (#3605). The pass is the media plugin's, which registers it under
// a key core names too, as it does the root of its local storage for backup, since neither references the other; a host
// without the plugin has nothing to run. The plugin lists and counts what it leaves, and the command then fails.
internal sealed class MediaSanitizeSvgCommand : HostCommand
{
    public const string DryRun = "--dry-run";

    // Dry run, output and error; the run answers how many SVGs it left as they are.
    public const string RunKey = "FluentCMS.Media.SanitizeSvg";

    public override string Name => "media sanitize-svg";

    public override string Description => "Sanitises every stored SVG of the media libraries again with the current pass and replaces each whose bytes change, as a replace of its file does; exits with 1 when it leaves one, such as one over maxSvgFileSize.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(DryRun, "Lists the SVGs whose bytes the pass would change, and those it would leave, and changes nothing."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var run = context.Services.GetKeyedService<Func<bool, TextWriter, TextWriter, CancellationToken, Task<int>>>(RunKey)
            ?? throw new CommandException(CommandErrorCodes.SvgsNotSanitized, "this host has no media plugin, so no stored SVG was read; run the command in the host that adds the plugin");

        var left = await run(context.Arguments.Has(DryRun), context.Output, context.Error, cancellationToken);
        if (left == 0)
        {
            return CommandExitCodes.Success;
        }

        throw new CommandException(
            CommandErrorCodes.SvgsNotSanitized,
            $"{left} stored SVGs are left as they are; raise the site's maxSvgFileSize for the run for those over it, and replace or delete by hand those the pass refuses or could not read, then run 'fluentcms media sanitize-svg' again");
    }
}
