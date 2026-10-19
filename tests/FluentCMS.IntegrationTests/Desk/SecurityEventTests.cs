using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Desk;

public sealed partial class SecurityEventTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_list_holds_the_named_operations_newest_first_with_the_total(string provider)
    {
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var services = TestServices.Build(database.Configuration, collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(null)));
        await TestServices.MigrateAsync(services);
        await SeedAsync(
            services,
            Row(Start, "agent_credential.issued"),
            Row(Start.AddHours(1), "change_set.opened"),
            Row(Start.AddHours(2), "agent.calls_per_period_exceeded"),
            Row(Start.AddHours(3), "user.disabled"),
            Row(Start.AddHours(4), "agent_credential.revoked"));

        var all = await ListAsync(services, new AuditEventListQuery { Operations = SecurityEvents.Operations });
        var page = await ListAsync(services, new AuditEventListQuery { Operations = SecurityEvents.Operations, Offset = 1, Limit = 2 });
        var recent = await ListAsync(services, new AuditEventListQuery { Operations = SecurityEvents.Operations, Since = Start.AddHours(2).ToOffset(TimeSpan.FromHours(-5)) });

        Assert.Equal(
            ["agent_credential.revoked", "user.disabled", "agent.calls_per_period_exceeded", "agent_credential.issued"],
            all.Items.Select(item => item.Operation));
        Assert.Equal(4, all.Total);
        Assert.Equal(["user.disabled", "agent.calls_per_period_exceeded"], page.Items.Select(item => item.Operation));
        Assert.Equal(4, page.Total);
        Assert.Equal(3, recent.Total);
        Assert.Equal(Start.AddHours(4), all.Items[0].Timestamp);
    }

    [Theory]
    [InlineData(-1, 50, AuditErrorCodes.OffsetInvalid)]
    [InlineData(0, 0, AuditErrorCodes.LimitInvalid)]
    [InlineData(0, 201, AuditErrorCodes.LimitInvalid)]
    public async Task A_window_out_of_range_or_no_operation_is_refused_and_a_list_appends_nothing(int offset, int limit, string code)
    {
        await using var database = await TemporaryDatabase.CreateAsync(DatabaseProviders.Sqlite);
        await using var services = TestServices.Build(database.Configuration, collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(null)));
        await TestServices.MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var lists = scope.ServiceProvider.GetRequiredService<IAuditEventLists>();

        var window = await lists.ListAsync(new AuditEventListQuery { Operations = SecurityEvents.Operations, Offset = offset, Limit = limit });
        var none = await lists.CountAsync(new AuditEventListQuery { Operations = [] });
        var listed = await lists.ListAsync(new AuditEventListQuery { Operations = SecurityEvents.Operations });

        Assert.Equal(code, Assert.Single(window.Errors).Code);
        Assert.Equal(AuditErrorCodes.OperationsMissing, Assert.Single(none.Errors).Code);
        Assert.Equal(0, listed.Value.Total);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.CountAsync());
    }

    [Fact]
    public async Task The_security_view_lists_key_cap_and_account_events_and_the_header_counts_them()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var key = await host.AsUserAsync<IAgentCredentialDirectory, IssuedAgentCredential>(ownerId, async credentials =>
            (await credentials.IssueAsync(new NewAgentCredential(
                "catalog-sync",
                DateTimeOffset.UtcNow.AddDays(30),
                AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read]).Value,
                AgentCaps.Create(10, TimeSpan.FromHours(1), 1m, TimeSpan.FromDays(1), 10, 10).Value))).Value);
        await host.AsUserAsync<IAgentCredentialDirectory, bool>(ownerId, async credentials =>
            (await credentials.RevokeAsync(key.Credential.Id)).IsSuccess);
        await AwaitingReviewAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var page = await host.Client.GetStringAsync("/security");
        var desk = await host.Client.GetStringAsync("/");

        Assert.Equal(
            ["agent_credential.revoked", "agent_credential.issued"],
            EventAttribute().Matches(page).Select(match => match.Groups[1].Value));
        Assert.Contains("An agent key was revoked", page);
        Assert.Contains($"href=\"audit?type=agent_credential&amp;id={key.Credential.Id}\"", page);
        Assert.DoesNotContain("change_set.opened", page);
        Assert.Matches("data-testid=\"nav-desk\"[^>]*>\\s*Desk\\s*<span[^>]*data-count=\"1\"", desk);
        Assert.Matches("data-testid=\"nav-security\"[^>]*>\\s*Security\\s*<span[^>]*data-count=\"2\"", desk);
        Assert.Contains("href=\"security\"", desk);
    }

    // SEC-08 end to end: each event is appended by the code that owns it, so a renamed operation drops off the view and fails here.
    // The password reset and a linked sign-in are read back the same way in PasswordResetTests and ExternalSignInTests.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_events_the_code_appends_reach_the_security_view_with_who_did_them(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(
            install,
            settings: database.Configuration,
            configure: builder => builder.Services.AddSingleton(McpAgents.WhoAmI));
        var ownerId = await host.CreateFirstAdministratorAsync();
        var oneAnHour = McpAgents.Credential(DateTimeOffset.UtcNow) with
        {
            Caps = AgentCaps.Create(1, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 200, 20).Value,
        };
        var issued = await McpAgents.IssueAsync(host, ownerId, oneAnHour);
        await using (var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key))
        {
            Assert.NotEqual(true, (await client.CallToolAsync("whoami")).IsError);
            Assert.True((await client.CallToolAsync("whoami")).IsError);
        }

        // An agent at its caps of one item per set and one set a day, which then tries to approve its own set.
        var capped = await McpAgents.IssueAsync(host, ownerId, McpAgents.Credential(DateTimeOffset.UtcNow, "capped-agent") with
        {
            Scope = AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update]).Value,
            Caps = AgentCaps.Create(600, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 1, 1).Value,
        });
        Guid cappedSet;
        await using (var client = await McpAgents.ConnectOverHttpAsync(host, capped.Key))
        {
            var opened = await client.CallToolAsync("open_change_set", new Dictionary<string, object?> { ["rationale"] = "Add the catalog types." });
            cappedSet = Guid.Parse((string)JsonNode.Parse(Text(opened))!["changeSet"]!["id"]!);
            foreach (var slug in new[] { "product", "category" })
            {
                await client.CallToolAsync("create_content_type", new Dictionary<string, object?>
                {
                    ["name"] = slug,
                    ["slug"] = slug,
                    ["definition"] = new JsonObject { ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = "text" }) },
                    ["change_set_id"] = cappedSet.ToString(),
                });
            }

            Assert.True((await client.CallToolAsync("open_change_set", new Dictionary<string, object?> { ["rationale"] = "One more." })).IsError);
            Assert.NotEqual(true, (await client.CallToolAsync("submit_change_set", new Dictionary<string, object?> { ["id"] = cappedSet.ToString() })).IsError);
        }

        var refusedMove = await AsAgentAsync(host, capped.Credential, directory => directory.TransitionAsync(cappedSet, ChangeSetState.Approved));
        Assert.Equal(AgentPermissionErrorCodes.TransitionNotPermitted, Assert.Single(refusedMove.Errors).Code);

        await host.AsUserAsync(ownerId, async users =>
        {
            var editor = (await users.CreateAsync(new NewUser("editor@example.com", "Editor", "An0ther!Passw0rd"))).Value;
            await users.DisableAsync(editor);
            return await users.EnableAsync(editor);
        });
        await host.AsUserAsync<IAgentCredentialDirectory, bool>(ownerId, async credentials => (await credentials.RevokeAsync(issued.Credential.Id)).IsSuccess);
        await host.SignInAsync(AdminHost.Owner);

        var rows = SecurityRows.Parse(await host.Client.GetStringAsync("/security"));

        Assert.Contains(("agent.calls_per_period_exceeded", "agent", "pipeline-agent"), rows);
        foreach (var operation in new[] { "agent.items_per_change_set_exceeded", "agent.change_sets_per_day_exceeded", "agent.transition_refused" })
        {
            Assert.Contains((operation, "agent", "capped-agent"), rows);
        }

        foreach (var operation in new[] { "agent_credential.issued", "agent_credential.revoked", "user.created", "user.disabled", "user.enabled" })
        {
            Assert.Contains((operation, "user", "Test administrator"), rows);
        }
    }

    [Fact]
    public async Task The_header_counts_follow_sets_and_events_as_they_change()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);
        var quiet = await host.Client.GetStringAsync("/");

        var set = await AwaitingReviewAsync(host, ownerId);
        await host.AsUserAsync(ownerId, async users => await users.DisableAsync(
            (await users.CreateAsync(new NewUser("editor@example.com", "Editor", "An0ther!Passw0rd"))).Value));
        var busy = await host.Client.GetStringAsync("/");
        await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.RejectAsync(set, "Not now.")).Value);
        var reviewed = await host.Client.GetStringAsync("/");

        Assert.Equal((0, 0), (HeaderCount(quiet, "nav-desk"), HeaderCount(quiet, "nav-security")));
        Assert.Equal((1, 2), (HeaderCount(busy, "nav-desk"), HeaderCount(busy, "nav-security")));
        Assert.Equal((0, 2), (HeaderCount(reviewed, "nav-desk"), HeaderCount(reviewed, "nav-security")));
    }

    [Fact]
    public async Task An_anonymous_request_for_the_security_view_is_sent_to_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var response = await host.Client.GetAsync("/security");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Fsecurity", host.LocationOf(response));
    }

    private static async Task<Guid> AwaitingReviewAsync(AdminHost host, Guid ownerId)
    {
        var type = await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async directory =>
            (await directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);
        return await host.AsUserAsync<IChangeSetDirectory, Guid>(ownerId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet("Add the product type.", []))).Value;
            await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
            await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).Value.Id;
        });
    }

    private static int HeaderCount(string page, string link)
    {
        var count = Regex.Match(page, $"data-testid=\"{link}\"[^>]*>\\s*\\w+\\s*<span[^>]*data-count=\"(\\d+)\"");
        return count.Success ? int.Parse(count.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    private static async Task<AuditEventList> ListAsync(ServiceProvider services, AuditEventListQuery query)
    {
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAuditEventLists>().ListAsync(query)).Value;
    }

    private static async Task SeedAsync(ServiceProvider services, params AuditEventRow[] rows)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        context.AuditEvents.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private static AuditEventRow Row(DateTimeOffset timestamp, string operation)
    {
        return new AuditEventRow
        {
            Id = Guid.CreateVersion7(),
            Timestamp = timestamp,
            InitiatorKind = InitiatorKinds.User,
            InitiatorId = Guid.NewGuid(),
            InitiatorName = "Dana",
            Operation = operation,
            SubjectType = "agent_credential",
            SubjectId = Guid.NewGuid(),
        };
    }

    [GeneratedRegex("data-event=\"([a-z_.]+)\"")]
    private static partial Regex EventAttribute();

    // Runs as the agent, as a call over MCP would, for a move no tool offers.
    private static async Task<FluentCMS.Core.Results.Result<ChangeSet>> AsAgentAsync(
        AdminHost host,
        AgentCredential credential,
        Func<IChangeSetDirectory, Task<FluentCMS.Core.Results.Result<ChangeSet>>> action)
    {
        var accessor = host.App.Services.GetRequiredService<IHttpContextAccessor>();
        await using var scope = host.App.Services.CreateAsyncScope();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = AgentPrincipal.For(credential, "test") };
        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result)
    {
        return Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
