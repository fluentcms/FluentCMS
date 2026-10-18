using FluentCMS.Core.Audit;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Audit;

public sealed class AuditReaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_window_includes_an_event_at_its_start_and_excludes_one_at_its_end(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        await log.AppendAsync(Start.AddMilliseconds(-1), "before.start");
        await log.AppendAsync(Start, "at.start");
        await log.AppendAsync(End.AddMilliseconds(-1), "before.end");
        await log.AppendAsync(End, "at.end");

        Assert.Equal(["at.start", "before.end"], Operations(await log.ReadAsync(new AuditQuery { From = Start, To = End })));
        Assert.Equal(["at.start", "before.end", "at.end"], Operations(await log.ReadAsync(new AuditQuery { From = Start })));
        Assert.Equal(["before.start", "at.start", "before.end"], Operations(await log.ReadAsync(new AuditQuery { To = End })));
        Assert.Empty(await log.ReadAsync(new AuditQuery { From = Start, To = Start }));

        // The same instants in other offsets select the same events.
        Assert.Equal(
            ["at.start", "before.end"],
            Operations(await log.ReadAsync(new AuditQuery { From = Start.ToOffset(TimeSpan.FromHours(-5)), To = End.ToOffset(new TimeSpan(3, 30, 0)) })));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_query_with_no_bounds_returns_the_full_log(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        await log.AppendAsync(DateTimeOffset.UnixEpoch, "first");
        await log.AppendAsync(Start, "second", new AuditSubject("entry", Guid.NewGuid()));
        await log.AppendAsync(new DateTimeOffset(2099, 12, 31, 23, 59, 59, TimeSpan.Zero), "third");

        Assert.Equal(["first", "second", "third"], Operations(await log.ReadAsync(new AuditQuery())));
    }

    // Whole seconds and fractions of different lengths: on SQLite the stored text has to sort in time order too.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Events_come_back_oldest_first_whatever_order_they_were_appended_in(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        long[] ticks = [5_000_000, 0, 2_500_000, 10_000_000, 1_250_000, 100_000_000, 10, 36_000_000_000];

        foreach (var offset in ticks)
        {
            await log.AppendAsync(Start.AddTicks(offset), $"at.{offset}");
        }

        Assert.Equal(ticks.Order().Select(offset => $"at.{offset}"), Operations(await log.ReadAsync(new AuditQuery())));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Events_with_the_same_timestamp_are_ordered_by_id(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        var appended = new List<AuditEvent>();

        for (var i = 0; i < 8; i++)
        {
            appended.Add(await log.AppendAsync(Start, $"tie.{i}"));
        }

        Assert.Equal(
            appended.Select(auditEvent => auditEvent.Id.ToString()).Order(StringComparer.Ordinal),
            (await log.ReadAsync(new AuditQuery())).Select(auditEvent => auditEvent.Id.ToString()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_items_history_holds_only_its_own_events_also_within_a_window(string provider)
    {
        await using var log = await AuditScenario.CreateAsync(provider);
        var price = new AuditSubject("entry", Guid.NewGuid());
        var otherEntry = new AuditSubject("entry", Guid.NewGuid());
        var sameIdOtherType = new AuditSubject("entity", price.Id);

        await log.AppendAsync(Start.AddDays(-1), "entry.created", price);
        await log.AppendAsync(Start, "entry.created", otherEntry);
        await log.AppendAsync(Start.AddDays(1), "entity.updated", sameIdOtherType);
        await log.AppendAsync(Start.AddDays(2), "entry.updated", price);
        await log.AppendAsync(Start.AddDays(3), "job.ran");
        await log.AppendAsync(End, "entry.published", price);

        var history = await log.ReadAsync(new AuditQuery { Subject = price });

        Assert.Equal(["entry.created", "entry.updated", "entry.published"], Operations(history));
        Assert.All(history, auditEvent => Assert.Equal(("entry", price.Id), (auditEvent.SubjectType, auditEvent.SubjectId)));
        Assert.Equal(["entry.updated"], Operations(await log.ReadAsync(new AuditQuery { Subject = price, From = Start, To = End })));
        Assert.Empty(await log.ReadAsync(new AuditQuery { Subject = new AuditSubject("entry", Guid.NewGuid()) }));
    }

    [Fact]
    public async Task An_invalid_query_fails_without_reading_or_writing()
    {
        await using var log = await AuditScenario.CreateAsync(DatabaseProviders.Sqlite);
        await using var scope = log.CreateScope();
        var inverted = new AuditQuery { From = End, To = Start, Subject = new AuditSubject(" ", Guid.Empty) };

        var read = scope.ServiceProvider.GetRequiredService<IAuditReader>().Read(inverted);

        Assert.False(read.IsSuccess);
        Assert.Equal(
            ["audit_window_inverted", "audit_subject_type_missing", "audit_subject_id_missing"],
            read.Errors.Select(error => error.Code));

        using var destination = new MemoryStream();
        var exported = await scope.ServiceProvider.GetRequiredService<IAuditExporter>().ExportAsync(inverted, AuditExportFormat.Csv, destination);

        Assert.Equal(read.Errors, exported.Errors);
        Assert.Equal(0, destination.Length);
    }

    private static IEnumerable<string> Operations(IEnumerable<AuditEvent> events)
    {
        return events.Select(auditEvent => auditEvent.Operation);
    }
}
