using FluentCMS.Core.Identity;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Webhooks.WebhookScenario;

namespace FluentCMS.IntegrationTests.Webhooks;

public sealed class WebhookDirectoryTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_person_adds_a_subscription_whose_secret_is_returned_once_and_stored_encrypted(string provider)
    {
        await using var scenario = await WebhookScenario.CreateAsync(provider);

        var added = (await scenario.RunAsync(directory => directory.AddAsync(Frontend()))).Value;
        var secret = added.Secret[WebhookSecrets.Prefix.Length..];

        Assert.StartsWith(WebhookSecrets.Prefix, added.Secret, StringComparison.Ordinal);
        Assert.Equal(32, Convert.FromBase64String(secret).Length);

        var listed = Assert.Single((await scenario.RunAsync(directory => directory.ListAsync())).Value);
        Assert.Equal(added.Subscription.Id, listed.Id);
        Assert.Equal(new Uri("https://hooks.example.com/fluentcms"), listed.Url);
        Assert.Equal([WebhookEvents.ChangeSetPublished, WebhookEvents.ChangeSetReverted], listed.Events);
        Assert.True(listed.Enabled);
        Assert.Equal((Start, Amir), (listed.AddedAt, listed.AddedBy));

        var row = Assert.Single(await scenario.RowsAsync());
        Assert.DoesNotContain(secret, row.ProtectedSecret, StringComparison.Ordinal);
        Assert.Equal(added.Secret, scenario.Unprotect(row.ProtectedSecret));
        Assert.False(await scenario.StoresTextAsync(secret));

        var addition = Assert.Single(await scenario.EventsAsync());
        Assert.Equal(("webhook.added", added.Subscription.Id, Amir.UserId), (addition.Operation, addition.SubjectId!.Value, addition.InitiatorId));

        Assert.Contains(scenario.Logs, line => line.Contains("\"WebhookSubscriptions\"", StringComparison.Ordinal));
        Assert.DoesNotContain(scenario.Logs, line => line.Contains(secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_allow_listed_host_its_subdomains_and_an_allow_listed_local_endpoint_are_accepted(string provider)
    {
        await using var scenario = await WebhookScenario.CreateAsync(provider);

        foreach (var url in new[] { "https://example.com/hooks", "https://deep.hooks.example.com/hooks", "http://localhost:5005/hooks" })
        {
            var added = await scenario.RunAsync(directory => directory.AddAsync(Frontend(url)));
            Assert.True(added.IsSuccess, string.Join("; ", added.Errors.Select(error => error.Message)));
        }

        Assert.Equal(3, (await scenario.RowsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_url_or_events_that_break_a_rule_are_refused_with_a_reason_and_nothing_is_stored(string provider)
    {
        await using var scenario = await WebhookScenario.CreateAsync(provider);
        var cases = new (NewWebhookSubscription Subscription, string[] Codes)[]
        {
            (Frontend("https://example.com.evil.test/hooks"), [WebhookErrorCodes.HostNotAllowed]),
            (Frontend("https://hooks.other.test/hooks"), [WebhookErrorCodes.HostNotAllowed]),
            (Frontend("http://hooks.example.com/hooks"), [WebhookErrorCodes.UrlNotHttps]),
            (Frontend("http://localhost:5006/hooks"), [WebhookErrorCodes.HostNotAllowed]),
            (Frontend("https://user:secret@hooks.example.com/hooks"), [WebhookErrorCodes.UrlHasCredentials]),
            (Frontend("hooks.example.com/hooks"), [WebhookErrorCodes.UrlInvalid]),
            (Frontend("ftp://hooks.example.com/hooks"), [WebhookErrorCodes.UrlInvalid]),
            (Frontend("https://hooks.example.com/" + new string('a', WebhookSubscription.MaxUrlLength)), [WebhookErrorCodes.UrlTooLong]),
            (new NewWebhookSubscription("https://hooks.example.com/hooks", []), [WebhookErrorCodes.EventsMissing]),
            (new NewWebhookSubscription("https://hooks.example.com/hooks", ["change_set.created"]), [WebhookErrorCodes.EventUnknown]),
            (new NewWebhookSubscription("http://hooks.other.test/hooks", []), [WebhookErrorCodes.UrlNotHttps, WebhookErrorCodes.HostNotAllowed, WebhookErrorCodes.EventsMissing]),
        };

        foreach (var (subscription, codes) in cases)
        {
            var refused = await scenario.RunAsync(directory => directory.AddAsync(subscription));

            Assert.Equal(codes, refused.Errors.Select(error => error.Code));
        }

        var hostRefusal = await scenario.RunAsync(directory => directory.AddAsync(Frontend("https://hooks.other.test/hooks")));
        Assert.Contains("Outbound__AllowedHosts", Assert.Single(hostRefusal.Errors).Message, StringComparison.Ordinal);
        Assert.Empty(await scenario.RowsAsync());
        Assert.Empty(await scenario.EventsAsync());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Disabling_marks_the_subscription_disabled_once_and_appends_its_event(string provider)
    {
        await using var scenario = await WebhookScenario.CreateAsync(provider);
        var added = (await scenario.RunAsync(directory => directory.AddAsync(Frontend()))).Value.Subscription;
        scenario.Clock.Now = Start.AddHours(1);

        var disabled = await scenario.RunAsync(directory => directory.DisableAsync(added.Id));
        var again = await scenario.RunAsync(directory => directory.DisableAsync(added.Id));
        var unknown = await scenario.RunAsync(directory => directory.DisableAsync(Guid.NewGuid()));

        Assert.Equal(Start.AddHours(1), disabled.Value.DisabledAt);
        Assert.False(Assert.Single((await scenario.RunAsync(directory => directory.ListAsync())).Value).Enabled);
        Assert.Equal(WebhookErrorCodes.AlreadyDisabled, Assert.Single(again.Errors).Code);
        Assert.Equal(WebhookErrorCodes.NotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(["webhook.added", "webhook.disabled"], (await scenario.EventsAsync()).OrderBy(row => row.Timestamp).Select(row => row.Operation));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_a_person_lists_adds_or_disables_a_subscription(string provider)
    {
        await using var scenario = await WebhookScenario.CreateAsync(provider);
        var added = (await scenario.RunAsync(directory => directory.AddAsync(Frontend()))).Value.Subscription;

        foreach (var initiator in new Initiator?[] { new AgentInitiator(Guid.NewGuid(), "example-agent"), SystemInitiator.Checks, null })
        {
            scenario.Initiator = initiator;

            var list = await scenario.RunAsync(directory => directory.ListAsync());
            var add = await scenario.RunAsync(directory => directory.AddAsync(Frontend("https://example.com/other")));
            var disable = await scenario.RunAsync(directory => directory.DisableAsync(added.Id));

            Assert.Equal(WebhookErrorCodes.PeopleOnly, Assert.Single(list.Errors).Code);
            Assert.Equal(WebhookErrorCodes.PeopleOnly, Assert.Single(add.Errors).Code);
            Assert.Equal(WebhookErrorCodes.PeopleOnly, Assert.Single(disable.Errors).Code);
        }

        var row = Assert.Single(await scenario.RowsAsync());
        Assert.Null(row.DisabledAt);
        Assert.Single(await scenario.EventsAsync());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_migration_down_drops_the_table_and_up_adds_it_again(string provider)
    {
        await using var scenario = await WebhookScenario.CreateAsync(provider);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().Database;
            var migrations = database.GetMigrations().ToList();
            var index = migrations.FindIndex(name => name.EndsWith("_WebhookSubscriptions", StringComparison.Ordinal));
            await database.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => scenario.QueryAsync(context => context.WebhookSubscriptions.AnyAsync()));

        await TestServices.MigrateAsync(scenario.Services);

        Assert.True((await scenario.RunAsync(directory => directory.AddAsync(Frontend()))).IsSuccess);
    }
}
