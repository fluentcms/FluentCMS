using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Desk;

public sealed class CheckCardTests
{
    // A check job that ended without moving its set leaves the set in checks running; the person who opened it recovers it from the
    // set view, which fails the set so it can be reopened.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_whose_check_job_ended_says_so_and_its_opener_recovers_it(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var type = await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(
            new NewContentType("Brand", "brand", new ContentTypeDefinition([new FieldDefinition("name", "text")])), TestChangeSetTargets.ContentModel)).Value);
        var setId = await CheckedSets.DeadJobAsync(host, ownerId, type);
        await host.SignInAsync(AdminHost.Owner);

        var page = await host.Client.GetStringAsync($"/sets/{setId}");
        Assert.Contains("data-testid=\"check-job-ended\"", page);
        Assert.DoesNotContain("The checks are running, so it cannot be reviewed yet.", page);
        Assert.DoesNotContain("items done", page);
        Assert.Contains("Run the checks again", page);

        var seen = Regex.Match(page, "name=\"Submit.Seen\" value=\"([0-9a-f]+)\"").Groups[1].Value;
        var recovered = await host.SubmitAsync($"/sets/{setId}", "submit", new Dictionary<string, string> { ["Submit.Seen"] = seen });

        var after = await recovered.Content.ReadAsStringAsync();
        Assert.Contains("data-state=\"checks-failed\"", after);
        Assert.Contains("now shows its checks as failed", after);
        Assert.DoesNotContain("Submitted. The checks run", after);
        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(setId)).Value);
        Assert.Equal(ChangeSetState.ChecksFailed, set.State);
    }

    // An agent submits its own sets by tool: a person's post of Submit for one is refused, whatever the form claims.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_post_submitting_an_agents_open_set_is_refused(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var issued = await Mcp.McpAgents.IssueAsync(host, ownerId, Mcp.McpAgents.Credential(DateTimeOffset.UtcNow, "catalog-agent") with
        {
            Scope = FluentCMS.Core.Agents.AgentScope.Create(
                [FluentCMS.Core.Sites.Site.Default.Id], null, null, [FluentCMS.Core.Agents.AgentOperation.Read, FluentCMS.Core.Agents.AgentOperation.Create]).Value,
        });
        Guid setId;
        await using (var client = await Mcp.McpAgents.ConnectOverHttpAsync(host, issued.Key))
        {
            var opened = await client.CallToolAsync("open_change_set", new Dictionary<string, object?> { ["rationale"] = "Add the brand type." });
            setId = Guid.Parse((string)System.Text.Json.Nodes.JsonNode.Parse(((ModelContextProtocol.Protocol.TextContentBlock)opened.Content.Single()).Text)!["changeSet"]!["id"]!);
        }

        await host.SignInAsync(AdminHost.Owner);
        var page = await host.Client.GetStringAsync($"/sets/{setId}");
        Assert.DoesNotContain("data-action=\"submit\"", page);
        var seen = await LastMoveAsync(host, setId);
        var refused = await host.SubmitAsync($"/sets/{setId}", "submit", new Dictionary<string, string> { ["Submit.Seen"] = seen });

        Assert.Contains("data-testid=\"review-refusal\"", await refused.Content.ReadAsStringAsync());
        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(setId)).Value);
        Assert.Equal(ChangeSetState.Open, set.State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failing_run_shows_its_findings_and_each_severity(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var setId = await CheckedSets.FailingAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var page = await host.Client.GetStringAsync($"/sets/{setId}");

        Assert.Contains("data-state=\"checks-failed\"", page);
        Assert.Contains("data-check=\"no-superlatives\" data-status=\"fail\"", page);
        Assert.Contains("blocks", page[page.IndexOf("data-check=\"no-superlatives\"", StringComparison.Ordinal)..]);
        var finding = page[page.IndexOf("data-finding", StringComparison.Ordinal)..];
        Assert.StartsWith("data-finding><bdi>", finding.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty));
    }

    private static async Task<string> LastMoveAsync(AdminHost host, Guid id)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents
            .AsNoTracking()
            .Where(row => row.SubjectType == "change_set" && row.SubjectId == id)
            .ToListAsync();
        return rows.OrderBy(row => row.Timestamp).Last(row => ChangeSetMoves.Operations.Contains(row.Operation)).Id.ToString("N");
    }
}
