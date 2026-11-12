using System.Text.Json;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Sample;

// The sample plugin's notes in a transfer package, as plugins/sample/notes.jsonl. Notes belong to no site, so every export
// carries all of them, and an import adds the ones the instance lacks.
public sealed class SampleTransfer(PluginDbContext<SamplePlugin> database) : ITransferContributor
{
    public const string Section = "sample-notes";
    public const string File = "notes.jsonl";

    public string Name => "sample";

    public async Task ExportAsync(ITransferExport context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var notes = await database.Set<SampleNote>().AsNoTracking().OrderBy(note => note.Text).ToListAsync(cancellationToken);
        await context.WriteAsync(
            File,
            async (stream, token) =>
            {
                foreach (var note in notes)
                {
                    await JsonSerializer.SerializeAsync(stream, note, cancellationToken: token);
                    await stream.WriteAsync("\n"u8.ToArray(), token);
                }
            },
            cancellationToken);
    }

    public async Task ImportAsync(ITransferImport context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<SampleNote> notes = [];
        await context.ReadAsync(
            File,
            async (stream, token) =>
            {
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
                {
                    notes.Add(JsonSerializer.Deserialize<SampleNote>(line)!);
                }
            },
            cancellationToken);

        foreach (var note in notes)
        {
            if (await database.Set<SampleNote>().AnyAsync(item => item.Id == note.Id, cancellationToken))
            {
                context.Report(Section, note.Id.ToString("D"), TransferOutcome.Skipped);
                continue;
            }

            if (!context.DryRun)
            {
                database.Add(note);
                await database.SaveChangesAsync(cancellationToken);
            }

            context.Report(Section, note.Id.ToString("D"), TransferOutcome.Created);
        }
    }
}
