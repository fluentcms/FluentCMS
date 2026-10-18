using System.Net;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Audit;

public sealed class AuditScreenTests
{
    [Fact]
    public async Task Anonymous_requests_for_the_page_or_the_download_go_to_sign_in_and_record_nothing()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        await AssertAnonymousRequestsGoToSignInAsync(host);
    }

    // An external provider adds a sign-in option, not a second way in: the admin's own sign-in page still answers every challenge.
    [Fact]
    public async Task With_an_external_provider_configured_anonymous_requests_still_go_to_sign_in()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, ["--environment", "Development"], new Dictionary<string, string?>
        {
            ["Authentication:OpenIdConnect:Authority"] = provider.Authority,
            ["Authentication:OpenIdConnect:ClientId"] = TestOpenIdProvider.ClientId,
            ["Authentication:OpenIdConnect:ClientSecret"] = TestOpenIdProvider.ClientSecret,
        });
        await host.CreateFirstAdministratorAsync();

        await AssertAnonymousRequestsGoToSignInAsync(host);
    }

    private static async Task AssertAnonymousRequestsGoToSignInAsync(AdminHost host)
    {
        var page = await host.Client.GetAsync("/audit");
        var download = await host.Client.PostAsync("/audit/export", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Format"] = "json",
        }));

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Faudit", host.LocationOf(page));
        Assert.Equal(HttpStatusCode.Redirect, download.StatusCode);
        Assert.StartsWith("/sign-in?", host.LocationOf(download));
        Assert.Empty(await RowsAsync(host, "audit.exported"));
    }

    [Fact]
    public async Task A_signed_in_download_returns_the_selection_as_json_or_csv_and_records_each_export()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var json = await DownloadAsync(host, new Dictionary<string, string>
        {
            ["SubjectType"] = "user",
            ["SubjectId"] = ownerId.ToString(),
            ["Format"] = "json",
        });

        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        Assert.Equal("application/json", json.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", json.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"audit-history-{ownerId}.json", FileName(json));
        Assert.True(json.Headers.CacheControl?.NoStore);
        Assert.True(json.Headers.Contains("Content-Security-Policy"));

        var history = ExportedEvents.FromJson(await json.Content.ReadAsStringAsync());
        Assert.Equal(["user.signed_in", "audit.exported"], history.Select(auditEvent => auditEvent.Operation));
        Assert.All(history, auditEvent => Assert.Equal(("user", ownerId), (auditEvent.SubjectType, auditEvent.SubjectId)));
        Assert.Equal(new UserInitiator(ownerId, AdminHost.Owner.DisplayName), history[1].Initiator);

        var today = DateTime.UtcNow.Date;
        var csv = await DownloadAsync(host, new Dictionary<string, string>
        {
            ["From"] = today.AddDays(-1).ToString("yyyy-MM-dd"),
            ["To"] = today.AddDays(1).ToString("yyyy-MM-dd"),
            ["Format"] = "csv",
        });

        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal("audit-log.csv", FileName(csv));

        var log = ExportedEvents.FromCsv(await csv.Content.ReadAsStringAsync());
        Assert.Equal(["user.signed_in", "audit.exported", "audit.exported"], log.Select(auditEvent => auditEvent.Operation));
        Assert.Null(log[2].SubjectType);

        var recorded = await RowsAsync(host, "audit.exported");
        Assert.Equal(2, recorded.Count);
        Assert.All(recorded, row => Assert.Equal((InitiatorKinds.User, ownerId), (row.InitiatorKind, row.InitiatorId)));
    }

    [Fact]
    public async Task A_rejected_export_returns_to_the_page_with_each_problem_next_to_its_field_and_records_nothing()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var rejected = await DownloadAsync(host, new Dictionary<string, string>
        {
            ["From"] = "2026-10-02",
            ["To"] = "2026-10-01",
            ["SubjectType"] = "entry",
            ["SubjectId"] = "not-an-id",
            ["Format"] = "json",
        });

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        var location = host.LocationOf(rejected)!;
        Assert.StartsWith("/audit?", location);

        var page = await host.Client.GetStringAsync(location);
        Assert.Contains("The end date is before the start date.", page);
        Assert.Contains("such as 3f2504e0-4f89-41d3-9a0c-0305e82c3301", page);
        Assert.Equal(2, Count(page, "aria-invalid=\"true\""));
        Assert.Contains("value=\"2026-10-02\"", page);
        Assert.Contains("value=\"entry\"", page);
        Assert.Contains("value=\"not-an-id\"", page);
        Assert.Empty(await RowsAsync(host, "audit.exported"));

        // A session that ran out lands back on the download's address after sign-in; it leads to the page.
        Assert.Equal("/audit", host.LocationOf(await host.Client.GetAsync("/audit/export")));
    }

    [Fact]
    public async Task An_export_post_without_a_valid_antiforgery_token_is_rejected_and_records_nothing()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var forged = await host.Client.PostAsync("/audit/export", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Format"] = "json",
            ["__RequestVerificationToken"] = "forged",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Empty(await RowsAsync(host, "audit.exported"));
    }

    [Fact]
    public async Task The_history_lookup_lists_an_items_events_oldest_first_with_every_field()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var entryId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var changeSetId = Guid.NewGuid();
        var previousVersionId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);
        var agentUpdate = Row(start, "entry.updated", entryId, InitiatorKinds.Agent, credentialId, "catalog-sync");
        agentUpdate.AgentCredentialId = credentialId;
        agentUpdate.Model = "example-model";
        agentUpdate.PromptHash = "sha256:9f86d081";
        agentUpdate.Sources = ["https://example.com/prices", "<script>alert(1)</script>"];
        agentUpdate.ChangeSetId = changeSetId;
        agentUpdate.PreviousVersionId = previousVersionId;
        await SeedAsync(
            host,
            Row(start.AddHours(2), "entry.approved", entryId, InitiatorKinds.User, Guid.NewGuid(), "Dana"),
            agentUpdate,
            Row(start.AddHours(1), "entry.updated", Guid.NewGuid(), InitiatorKinds.User, Guid.NewGuid(), "Someone else"));

        var home = await host.Client.GetStringAsync("/");
        Assert.Contains("href=\"audit\"", home);

        var page = await host.Client.GetStringAsync($"/audit?type=entry&id={entryId}");
        var events = page[page.IndexOf("data-testid=\"audit-history-events\"", StringComparison.Ordinal)..];

        Assert.True(events.IndexOf("entry.updated", StringComparison.Ordinal) < events.IndexOf("entry.approved", StringComparison.Ordinal));
        Assert.DoesNotContain("Someone else", events);
        Assert.Contains(">Agent</span>", events);
        Assert.Contains(">User</span>", events);
        Assert.Contains("catalog-sync", events);
        Assert.Contains("2026-10-01 09:30:00", events);
        Assert.Contains(credentialId.ToString(), events);
        Assert.Contains("example-model", events);
        Assert.Contains("sha256:9f86d081", events);
        Assert.Contains("https://example.com/prices", events);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", events);
        Assert.DoesNotContain("<script>alert(1)", events);
        Assert.Contains(changeSetId.ToString(), events);
        Assert.Contains(previousVersionId.ToString(), events);

        var nothing = await host.Client.GetStringAsync($"/audit?type=entry&id={Guid.NewGuid()}");
        Assert.Contains("data-testid=\"audit-history-empty\"", nothing);

        var malformed = await host.Client.GetStringAsync("/audit?type=entry&id=nope");
        Assert.Contains("aria-invalid=\"true\"", malformed);
        Assert.DoesNotContain("data-testid=\"audit-history-events\"", malformed);
    }

    private static async Task<HttpResponseMessage> DownloadAsync(AdminHost host, Dictionary<string, string> fields)
    {
        return await host.SubmitAsync("/audit/export", "audit-export", fields, from: "/audit");
    }

    private static string? FileName(HttpResponseMessage response)
    {
        return response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
    }

    private static int Count(string text, string value)
    {
        return (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
    }

    private static AuditEventRow Row(DateTimeOffset at, string operation, Guid entryId, string kind, Guid initiatorId, string name)
    {
        return new AuditEventRow
        {
            Id = Guid.CreateVersion7(at),
            Timestamp = at,
            InitiatorKind = kind,
            InitiatorId = initiatorId,
            InitiatorName = name,
            Operation = operation,
            SubjectType = "entry",
            SubjectId = entryId,
        };
    }

    // Events from initiators the web host has no way to act as yet, such as an agent, go straight into the table.
    private static async Task SeedAsync(AdminHost host, params AuditEventRow[] rows)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        context.AuditEvents.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private static async Task<List<AuditEventRow>> RowsAsync(AdminHost host, string operation)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents
            .Where(row => row.Operation == operation)
            .ToListAsync();
    }
}
