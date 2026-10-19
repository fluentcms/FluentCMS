using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
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

    private static async Task AwaitingReviewAsync(AdminHost host, Guid ownerId)
    {
        var type = await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async directory =>
            (await directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition([new FieldDefinition("title", "text")])))).Value);
        await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet("Add the product type.", []))).Value;
            await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
            await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).Value;
        });
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
}
