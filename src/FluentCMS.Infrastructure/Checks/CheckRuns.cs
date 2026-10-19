using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Checks;

// Runs are written with the caller's save inside its unit of work, so a run and the move it decides commit together or not at all.
internal sealed class CheckRuns(FluentCmsDbContext context) : ICheckRuns
{
    public async Task<ChangeSetCheckRun> RecordAsync(ChangeSetCheckRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A check run is recorded inside the unit of work that moves its change set (CHK-03).");
        }

        var latest = await context.CheckRuns.AsNoTracking()
            .Where(row => row.ChangeSetId == run.ChangeSetId)
            .MaxAsync(row => (int?)row.Number, cancellationToken);
        var numbered = run with { Number = (latest ?? 0) + 1 };

        context.CheckRuns.Add(new CheckRunRow
        {
            Id = numbered.Id,
            ChangeSetId = numbered.ChangeSetId,
            Number = numbered.Number,
            Mode = numbered.Mode,
            StartedAt = numbered.StartedAt,
            FinishedAt = numbered.FinishedAt,
            Status = numbered.Status,
            Cost = numbered.Cost,
            Problems = JsonSerializer.Serialize(numbered.Problems),
        });

        var position = 0;
        foreach (var item in numbered.Items)
        {
            foreach (var result in item.Results)
            {
                context.CheckResults.Add(new CheckResultRow
                {
                    Id = Guid.CreateVersion7(),
                    RunId = numbered.Id,
                    Position = position++,
                    ItemKind = item.ItemKind,
                    ItemId = item.ItemId,
                    ItemVersion = item.Version,
                    ContentType = item.ContentType,
                    CheckId = result.CheckId,
                    Check = result.Check,
                    CheckVersion = result.Version,
                    CheckType = result.Type,
                    Kind = result.Kind,
                    Severity = result.Severity,
                    Description = result.Description,
                    Status = result.Status,
                    Findings = JsonSerializer.Serialize(result.Findings.Select(finding => new StoredFinding(finding.Message, finding.Pointer, finding.Excerpt))),
                    ModelCallId = result.Model?.CallId,
                    Provider = result.Model?.Provider,
                    Model = result.Model?.Model,
                    PromptHash = result.Model?.PromptHash,
                    Cost = result.Model?.Cost ?? 0,
                });
            }
        }

        return numbered;
    }

    public async Task<ChangeSetCheckRun?> LatestAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        var run = await context.CheckRuns.AsNoTracking()
            .Where(row => row.ChangeSetId == changeSetId)
            .OrderByDescending(row => row.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        var results = await context.CheckResults.AsNoTracking()
            .Where(row => row.RunId == run.Id)
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken);

        // An item with no check that applies has no rows, so items are the ones that were judged.
        var items = results
            .GroupBy(row => (row.ItemKind, row.ItemId, row.ItemVersion, row.ContentType))
            .Select(group => new ItemChecks(group.Key.ItemKind, group.Key.ItemId, group.Key.ItemVersion, group.Key.ContentType, group.Select(ToResult).ToList()))
            .ToList();

        var problems = JsonSerializer.Deserialize<List<string>>(run.Problems) ?? [];
        return new ChangeSetCheckRun(run.Id, run.ChangeSetId, run.Number, run.Mode, run.StartedAt, run.FinishedAt, run.Status, run.Cost, items, problems);
    }

    private static CheckResult ToResult(CheckResultRow row)
    {
        var findings = JsonSerializer.Deserialize<List<StoredFinding>>(row.Findings) ?? [];
        var model = row.ModelCallId is { } callId ? new CheckModelUse(callId, row.Provider!, row.Model!, row.PromptHash!, row.Cost) : null;

        return new CheckResult(
            row.CheckId,
            row.Check,
            row.CheckVersion,
            row.CheckType,
            row.Kind,
            row.Severity,
            row.Description,
            row.Status,
            findings.Select(finding => new CheckFinding(finding.Message, finding.Pointer, finding.Excerpt)).ToList(),
            model);
    }

    private sealed record StoredFinding(string Message, string Pointer, string? Excerpt);
}
