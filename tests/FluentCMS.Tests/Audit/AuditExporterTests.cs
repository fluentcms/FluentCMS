using System.Text;
using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Audit;

public sealed class AuditExporterTests
{
    private const string Header =
        "id,timestamp,operation,initiatorKind,initiatorId,initiatorName,agentCredentialId,subjectType,subjectId,model,promptHash,sources,changeSetId,previousVersionId\r\n";

    private static readonly UserInitiator Amir = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "Amir");
    private static readonly AgentInitiator CatalogSync = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), "catalog-sync");

    private static readonly AuditEvent Full = new()
    {
        Id = Guid.Parse("01a0f4c2-c47b-743f-840b-3b6259891ca7"),
        Timestamp = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero).AddTicks(5_000_000),
        Initiator = CatalogSync,
        Operation = "entry.updated",
        SubjectType = "entry",
        SubjectId = Guid.Parse("95c1ae39-4c1f-4d76-b35e-4536359ea9ad"),
        Model = "claude-sonnet-4-5",
        PromptHash = "sha256:9f86d081",
        Sources = ["https://example.com/prices", "Dana"],
        ChangeSetId = Guid.Parse("7c3c47c6-9c06-4854-81aa-7aa6d2e54ba4"),
        PreviousVersionId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"),
    };

    private static readonly AuditEvent Minimal = new()
    {
        Id = Guid.Parse("01a0f4c2-c7e8-7eda-98a9-9a8b198f5b07"),
        Timestamp = new DateTimeOffset(2026, 10, 17, 13, 0, 0, TimeSpan.Zero),
        Initiator = Amir,
        Operation = "job.ran",
    };

    [Fact]
    public async Task Json_is_an_array_of_objects_with_every_field_under_the_shared_names()
    {
        var (result, json) = await ExportAsync([Full, Minimal], AuditExportFormat.Json);

        Assert.True(result.IsSuccess);
        using var document = JsonDocument.Parse(json);
        var events = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, events.Count);

        var full = events[0];
        Assert.Equal(Header.TrimEnd().Split(','), full.EnumerateObject().Select(property => property.Name));
        Assert.Equal("01a0f4c2-c47b-743f-840b-3b6259891ca7", full.GetProperty("id").GetString());
        Assert.Equal("2026-10-17T12:00:00.5000000+00:00", full.GetProperty("timestamp").GetString());
        Assert.Equal("entry.updated", full.GetProperty("operation").GetString());
        Assert.Equal("agent", full.GetProperty("initiatorKind").GetString());
        Assert.Equal("7c9e6679-7425-40de-944b-e07fc1f90ae7", full.GetProperty("initiatorId").GetString());
        Assert.Equal("catalog-sync", full.GetProperty("initiatorName").GetString());
        Assert.Equal("7c9e6679-7425-40de-944b-e07fc1f90ae7", full.GetProperty("agentCredentialId").GetString());
        Assert.Equal("entry", full.GetProperty("subjectType").GetString());
        Assert.Equal("95c1ae39-4c1f-4d76-b35e-4536359ea9ad", full.GetProperty("subjectId").GetString());
        Assert.Equal("claude-sonnet-4-5", full.GetProperty("model").GetString());
        Assert.Equal("sha256:9f86d081", full.GetProperty("promptHash").GetString());
        Assert.Equal(["https://example.com/prices", "Dana"], full.GetProperty("sources").EnumerateArray().Select(source => source.GetString()));
        Assert.Equal("7c3c47c6-9c06-4854-81aa-7aa6d2e54ba4", full.GetProperty("changeSetId").GetString());
        Assert.Equal("3f2504e0-4f89-41d3-9a0c-0305e82c3301", full.GetProperty("previousVersionId").GetString());

        var minimal = events[1];
        Assert.Equal("user", minimal.GetProperty("initiatorKind").GetString());
        Assert.Equal(JsonValueKind.Array, minimal.GetProperty("sources").ValueKind);
        Assert.Empty(minimal.GetProperty("sources").EnumerateArray());
        Assert.All(
            ["agentCredentialId", "subjectType", "subjectId", "model", "promptHash", "changeSetId", "previousVersionId"],
            name => Assert.Equal(JsonValueKind.Null, minimal.GetProperty(name).ValueKind));
    }

    [Fact]
    public async Task Csv_has_the_same_names_as_its_header_and_one_row_per_event_with_empty_fields_as_unquoted_empty_cells()
    {
        var (result, csv) = await ExportAsync([Full, Minimal], AuditExportFormat.Csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Header
            + "01a0f4c2-c47b-743f-840b-3b6259891ca7,2026-10-17T12:00:00.5000000+00:00,entry.updated,agent,7c9e6679-7425-40de-944b-e07fc1f90ae7,"
            + "catalog-sync,7c9e6679-7425-40de-944b-e07fc1f90ae7,entry,95c1ae39-4c1f-4d76-b35e-4536359ea9ad,claude-sonnet-4-5,sha256:9f86d081,"
            + "\"[\"\"https://example.com/prices\"\",\"\"Dana\"\"]\",7c3c47c6-9c06-4854-81aa-7aa6d2e54ba4,3f2504e0-4f89-41d3-9a0c-0305e82c3301\r\n"
            + "01a0f4c2-c7e8-7eda-98a9-9a8b198f5b07,2026-10-17T13:00:00.0000000+00:00,job.ran,user,0f8fad5b-d9cb-469f-a165-70867728950e,Amir,,,,,,[],,\r\n",
            csv);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("one\ntwo", "\"one\ntwo\"")]
    [InlineData("one\r\ntwo", "\"one\r\ntwo\"")]
    [InlineData(" padded ", "\" padded \"")]
    [InlineData("", "\"\"")]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tindented", "'\tindented")]
    [InlineData("\rreturn", "\"'\rreturn\"")]
    [InlineData("'quoted", "''quoted")]
    [InlineData("=a,b", "\"'=a,b\"")]
    public async Task Csv_escapes_awkward_values_so_they_read_back_exactly(string value, string cell)
    {
        var (_, csv) = await ExportAsync([Minimal with { Model = value }], AuditExportFormat.Csv);

        Assert.Equal(
            Header + $"01a0f4c2-c7e8-7eda-98a9-9a8b198f5b07,2026-10-17T13:00:00.0000000+00:00,job.ran,user,0f8fad5b-d9cb-469f-a165-70867728950e,Amir,,,,{cell},,[],,\r\n",
            csv);
    }

    [Fact]
    public async Task Sources_are_one_csv_cell_holding_a_json_array_and_a_json_array_in_json()
    {
        string[] sources = ["https://example.com/prices?region=eu,us", "Dana \"fact owner\"", "line\nbreak", "=1+1", "Zoë's notes"];
        var auditEvent = Minimal with { Sources = sources };

        var (_, csv) = await ExportAsync([auditEvent], AuditExportFormat.Csv);
        var (_, json) = await ExportAsync([auditEvent], AuditExportFormat.Json);

        const string cell = """""
            "[""https://example.com/prices?region=eu,us"",""Dana \""fact owner\"""",""line\nbreak"",""=1+1"",""Zoë's notes""]"
            """"";
        Assert.Contains($",Amir,,,,,,{cell},,\r\n", csv);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(sources, document.RootElement[0].GetProperty("sources").EnumerateArray().Select(source => source.GetString()));
    }

    [Fact]
    public async Task An_empty_selection_is_an_empty_array_or_a_header_alone()
    {
        Assert.Equal("[]", (await ExportAsync([], AuditExportFormat.Json)).Output);
        Assert.Equal(Header, (await ExportAsync([], AuditExportFormat.Csv)).Output);
    }

    [Theory]
    [InlineData(AuditExportFormat.Json)]
    [InlineData(AuditExportFormat.Csv)]
    public async Task Output_reaches_the_destination_while_events_are_still_being_read(AuditExportFormat format)
    {
        using var destination = new MemoryStream();
        long writtenBeforeLastEvent = 0;

        async IAsyncEnumerable<AuditEvent> ManyEvents()
        {
            for (var i = 0; i < 2_000; i++)
            {
                yield return Full with { Id = Guid.CreateVersion7(), Timestamp = Full.Timestamp.AddSeconds(i) };
                await Task.Yield();
            }

            writtenBeforeLastEvent = destination.Length;
        }

        var result = await ExporterOver(new StubReader(ManyEvents)).ExportAsync(new AuditQuery(), format, destination);

        Assert.True(result.IsSuccess);
        Assert.True(destination.CanWrite, "The destination is left open.");
        Assert.True(
            writtenBeforeLastEvent > destination.Length / 2,
            $"{writtenBeforeLastEvent} of {destination.Length} bytes reached the destination before the last event was read.");
    }

    [Fact]
    public async Task An_unknown_format_or_an_invalid_query_fails_before_anything_is_read_or_written()
    {
        var read = false;
        async IAsyncEnumerable<AuditEvent> Tracked()
        {
            read = true;
            await Task.CompletedTask;
            yield break;
        }

        var exporter = ExporterOver(new StubReader(Tracked));
        using var destination = new MemoryStream();

        var unknown = await exporter.ExportAsync(new AuditQuery(), (AuditExportFormat)42, destination);
        var inverted = await exporter.ExportAsync(
            new AuditQuery { From = Minimal.Timestamp, To = Full.Timestamp },
            AuditExportFormat.Json,
            destination);

        Assert.Equal("audit_export_format_unknown", Assert.Single(unknown.Errors).Code);
        Assert.Equal("audit_window_inverted", Assert.Single(inverted.Errors).Code);
        Assert.False(read);
        Assert.Equal(0, destination.Length);
    }

    private static async Task<(Result Result, string Output)> ExportAsync(AuditEvent[] events, AuditExportFormat format)
    {
        using var destination = new MemoryStream();
        var result = await ExporterOver(new StubReader(events.ToAsyncEnumerable)).ExportAsync(new AuditQuery(), format, destination);

        return (result, Encoding.UTF8.GetString(destination.ToArray()));
    }

    // Resolved the way the host does, so the Core registration is covered too.
    private static IAuditExporter ExporterOver(IAuditReader reader)
    {
        return new ServiceCollection()
            .AddFluentCmsCore()
            .AddScoped(_ => reader)
            .BuildServiceProvider()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<IAuditExporter>();
    }

    private sealed class StubReader(Func<IAsyncEnumerable<AuditEvent>> events) : IAuditReader
    {
        public Result<IAsyncEnumerable<AuditEvent>> Read(AuditQuery query)
        {
            var valid = query.Validate();
            return valid.IsSuccess
                ? Result<IAsyncEnumerable<AuditEvent>>.Success(events())
                : Result<IAsyncEnumerable<AuditEvent>>.Failure(valid.Errors);
        }
    }
}
