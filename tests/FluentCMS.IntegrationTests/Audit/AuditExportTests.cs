using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.IntegrationTests.Audit;

public sealed class AuditExportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly AgentInitiator CatalogSync = new(Guid.NewGuid(), "catalog-sync");
    private static readonly UserInitiator AwkwardOwner = new(Guid.NewGuid(), "@owner, \"Dana\"\r\nPricing");

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_single_items_history_can_be_reconstructed_from_a_json_or_csv_export_of_a_range(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        var price = new AuditSubject("entry", Guid.NewGuid());
        var other = new AuditSubject("entry", Guid.NewGuid());
        var changeSetId = Guid.NewGuid();

        await log.AppendAsync(Start.AddHours(-1), "entry.created", price);
        var expected = new List<AuditEvent>
        {
            await log.AppendAsync(Start.AddHours(1), new NewAuditEvent("entry.updated")
            {
                SubjectType = price.Type,
                SubjectId = price.Id,
                Model = "example-model",
                PromptHash = "sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                Sources =
                [
                    "https://example.com/prices?region=eu,us",
                    "Dana \"fact owner\"",
                    "line one\r\nline two",
                    "=HYPERLINK(\"https://example.com\")",
                    "'quoted",
                    " padded ",
                    "Zoë's notes",
                ],
                ChangeSetId = changeSetId,
                PreviousVersionId = Guid.NewGuid(),
            }, CatalogSync),
        };

        await log.AppendAsync(Start.AddHours(2), new NewAuditEvent("entry.updated") { SubjectType = other.Type, SubjectId = other.Id, ChangeSetId = changeSetId }, CatalogSync);
        await log.AppendAsync(Start.AddHours(3), "job.ran");

        expected.Add(await log.AppendAsync(
            Start.AddHours(4),
            new NewAuditEvent("entry.approved") { SubjectType = price.Type, SubjectId = price.Id, Model = "", ChangeSetId = changeSetId },
            AwkwardOwner));
        expected.Add(await log.AppendAsync(Start.AddHours(5), "-entry.published,+again", price));

        await log.AppendAsync(End, "entry.archived", price);

        var range = new AuditQuery { From = Start, To = End };
        var fromJson = ExportedEvents.FromJson(await log.ExportAsync(range, AuditExportFormat.Json));
        var fromCsv = ExportedEvents.FromCsv(await log.ExportAsync(range, AuditExportFormat.Csv));
        var history = await log.ReadAsync(range with { Subject = price });

        Assert.Equal(5, fromJson.Count);
        Assert.Equal(5, fromCsv.Count);
        Assert.Equal(expected.Select(ExportedEvents.Fingerprint), history.Select(ExportedEvents.Fingerprint));
        Assert.Equal(expected.Select(ExportedEvents.Fingerprint), OnlyThe(price, fromJson).Select(ExportedEvents.Fingerprint));
        Assert.Equal(expected.Select(ExportedEvents.Fingerprint), OnlyThe(price, fromCsv).Select(ExportedEvents.Fingerprint));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_items_history_exports_on_its_own_and_an_empty_selection_exports_nothing(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        var price = new AuditSubject("entry", Guid.NewGuid());
        var expected = new List<AuditEvent>
        {
            await log.AppendAsync(Start, "entry.created", price),
        };
        await log.AppendAsync(Start.AddHours(1), "entry.created", new AuditSubject("entry", Guid.NewGuid()));
        expected.Add(await log.AppendAsync(Start.AddHours(2), "entry.updated", price));

        var item = new AuditQuery { Subject = price };

        Assert.Equal(expected.Select(ExportedEvents.Fingerprint), ExportedEvents.FromJson(await log.ExportAsync(item, AuditExportFormat.Json)).Select(ExportedEvents.Fingerprint));
        Assert.Equal(expected.Select(ExportedEvents.Fingerprint), ExportedEvents.FromCsv(await log.ExportAsync(item, AuditExportFormat.Csv)).Select(ExportedEvents.Fingerprint));

        var nothing = new AuditQuery { To = Start };

        Assert.Equal("[]", await log.ExportAsync(nothing, AuditExportFormat.Json));
        Assert.Single(ExportedEvents.ParseCsv(await log.ExportAsync(nothing, AuditExportFormat.Csv)));
    }

    private static IEnumerable<AuditEvent> OnlyThe(AuditSubject subject, IEnumerable<AuditEvent> events)
    {
        return events.Where(auditEvent => auditEvent.SubjectType == subject.Type && auditEvent.SubjectId == subject.Id);
    }
}
