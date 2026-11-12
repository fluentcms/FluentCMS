using System.Text.Json;
using FluentCMS.Core.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The import of the transfer endpoint, run inside the host on a package file (spec 4.14). It prints what the endpoint
// answers: the progress on standard error and the report at the end, and exits with 1 when the report has a conflict or
// an error, on a dry run too.
internal sealed class ImportCommand : HostCommand
{
    public const string DryRun = "--dry-run";
    public const string Overwrite = "--overwrite";
    public const string Webhooks = "--webhooks";
    public const string MissingLocales = "--missing-locales";
    public const string Roles = "--roles";
    public const string Json = "--json";

    private static readonly string[] MissingLocaleChoices = ["refuse", "skip"];

    public override string Name => "import";

    public override string Description => "Imports an export package into a site and prints the report; exits with 1 when it has a conflict or an error.";

    public override IReadOnlyList<string> Arguments => ["file"];

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(DryRun, "Checks everything and writes nothing; the report is the one the import would give."),
        TransferCommands.SiteOption("import into"),
        new(TransferCommands.Types, "The content types to import, separated by commas; every type of the package when left out.", "list"),
        new(Overwrite, "Takes the package's copy of an entry that is newer in the instance, which is otherwise a conflict."),
        new(Webhooks, "Delivers the webhooks of what the import writes, which are otherwise not delivered."),
        new(MissingLocales, "refuse (the default) or skip: whether the locales the site does not allow refuse the import or are left out.", "mode"),
        new(Roles, "Imports the roles of the package too."),
        new(Json, "Writes the report as the endpoint's last line of JSON: {\"report\":{..}}."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var arguments = context.Arguments;
        var missing = arguments.Value(MissingLocales);
        if (missing is not null && !MissingLocaleChoices.Contains(missing, StringComparer.Ordinal))
        {
            throw new CommandException(CommandErrorCodes.BadUsage, $"{MissingLocales} is refuse or skip", CommandExitCodes.BadUsage);
        }

        var path = Path.GetFullPath(arguments.Positional[0]);
        FileStream package;
        try
        {
            package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CommandException(TransferCommandErrorCodes.FileNotRead, $"{path} could not be read: {exception.Message}");
        }

        await using (package)
        {
            using var site = await TransferCommands.EnterSiteAsync(context, cancellationToken);
            var request = new TransferImportRequest
            {
                DryRun = arguments.Has(DryRun),
                Overwrite = arguments.Has(Overwrite),
                SkipMissingLocales = missing == "skip",
                Webhooks = arguments.Has(Webhooks),
                Roles = arguments.Has(Roles),
                Types = TransferCommands.TypesOf(arguments),
            };

            var importer = context.Services.GetRequiredService<TransferImporter>();
            await using var run = await TransferCommands.Refusing(() => importer.PrepareAsync(package, package.Length, request, cancellationToken));
            var result = await importer.RunAsync(
                run,
                (line, _) => ImportOutput.WriteProgressAsync(JsonSerializer.SerializeToElement(line, line.GetType(), TransferPackage.Json), context.Error),
                cancellationToken);

            // The endpoint's last line, written the way it writes it.
            var report = JsonSerializer.Serialize(new { report = result }, TransferPackage.Json);
            return await ImportOutput.WriteReportAsync(report, arguments.Has(Json), context.Output, context.Error);
        }
    }
}
