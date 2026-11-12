using FluentCMS.Core.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The export of the transfer endpoint, run inside the host and written to a file (spec 4.14). The options are the
// endpoint's body, so the package is the one `fluentcms export --url` gets for the same options.
internal sealed class ExportCommand : HostCommand
{
    public const string Output = "--output";
    public const string PublishedOnly = "--published-only";
    public const string Roles = "--roles";
    public const string MediaFiles = "--media-files";

    public override string Name => "export";

    public override string Description => "Writes an export package of a site: its content, settings and, when asked, its roles and the media files.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Output, "The package file to write.", "file"),
        TransferCommands.SiteOption("export"),
        new(TransferCommands.Types, "The content types to export, separated by commas; every type when left out.", "list"),
        new(PublishedOnly, "Leaves out the drafts and the entries that are not published."),
        new(Roles, "Adds the site's roles."),
        new(MediaFiles, "Adds the files of the media assets, not only their metadata."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var file = context.Arguments.Value(Output)
            ?? throw new CommandException(CommandErrorCodes.BadUsage, $"'fluentcms {Name}' needs {Output}", CommandExitCodes.BadUsage);
        var path = Path.GetFullPath(file);

        using var site = await TransferCommands.EnterSiteAsync(context, cancellationToken);
        var request = new TransferExportRequest
        {
            Types = TransferCommands.TypesOf(context.Arguments),
            PublishedOnly = context.Arguments.Has(PublishedOnly),
            Roles = context.Arguments.Has(Roles),
            Options = context.Arguments.Has(MediaFiles) ? new Dictionary<string, string> { ["mediaFiles"] = "true" } : new Dictionary<string, string>(),
        };

        var exporter = context.Services.GetRequiredService<TransferExporter>();
        var run = await TransferCommands.Refusing(() => exporter.PrepareAsync(request, cancellationToken));

        // Written beside the file and moved over it once complete, so a failed export leaves no part of a package behind.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        FileStream stream;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // WriteAsync, which releases the lock of the run, does not run.
            await run.Lock.DisposeAsync();
            throw NotWritten(path, exception);
        }

        try
        {
            await using (stream)
            {
                await exporter.WriteAsync(run, stream, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw NotWritten(path, exception);
        }
        finally
        {
            File.Delete(temporary);
        }

        await context.Output.WriteLineAsync($"Wrote {path}");
        return CommandExitCodes.Success;
    }

    private static CommandException NotWritten(string path, Exception exception) =>
        new(CommandErrorCodes.OutputNotWritten, $"{path} could not be written: {exception.Message}");
}
