using FluentCMS.Core.Checks;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Schema;
using Microsoft.EntityFrameworkCore;
using static FluentCMS.IntegrationTests.Checks.CheckScenario;

namespace FluentCMS.IntegrationTests.Checks;

public sealed class CheckDirectoryTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_created_check_is_version_one_not_active_and_audited(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);

        var created = (await scenario.CreateAsync("no-superlatives", NoSuperlatives())).Value;
        var read = (await scenario.RunAsync(directory => directory.GetAsync("no-superlatives"))).Value;

        Assert.Equal((Site.Default.Id, 1, 1, (int?)null), (created.SiteId, created.Version, created.LatestVersion, created.ActiveVersion));
        Assert.Equal((created.Id, "no-superlatives", 1, (int?)null), (read.Id, read.Name, read.Version, read.ActiveVersion));
        Assert.Equal(NoSuperlatives().ToJson(), read.Definition.ToJson());
        Assert.Equal(
            [new CheckSummary(created.Id, "no-superlatives", 1, null)],
            await scenario.RunAsync(directory => directory.ListAsync()));
        Assert.Empty(await scenario.RunAsync(directory => directory.ListActiveAsync()));

        var auditEvent = Assert.Single(await scenario.AuditRowsAsync());
        Assert.Equal(("check.created", created.Id, Amir.UserId), (auditEvent.Operation, auditEvent.SubjectId, auditEvent.InitiatorId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_invalid_check_is_refused_with_every_problem_and_nothing_is_stored(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        var definition = new CheckDefinition(
            "sample.unknown",
            "Refused.",
            null,
            [Example("same name", CheckVerdict.Pass, "{}"), Example("Same Name", CheckVerdict.Pass, "{}")]);
        var invalidSettings = new CheckDefinition("sample.bannedWords", "Refused.", Parse("""{"words": []}"""), NoSuperlatives().Examples);

        var refused = await scenario.CreateAsync("no-superlatives", definition);
        var badSettings = await scenario.CreateAsync("no-superlatives", invalidSettings);
        var badName = await scenario.CreateAsync("No Superlatives", NoSuperlatives());

        Assert.Equal(
            [CheckErrorCodes.CheckTypeUnknown, CheckErrorCodes.ExamplesMissing, CheckErrorCodes.ExampleInvalid],
            refused.Errors.Select(error => error.Code));
        Assert.Equal("The setting 'words' must be a non-empty array of distinct, non-empty strings.", Assert.Single(badSettings.Errors).Message);
        Assert.Equal(CheckErrorCodes.NameInvalid, Assert.Single(badName.Errors).Code);
        Assert.Empty(await scenario.RunAsync(directory => directory.ListAsync()));
        Assert.Empty(await scenario.AuditRowsAsync());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_name_is_unique_within_the_site(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", NoSuperlatives());

        var second = await scenario.CreateAsync("no-superlatives", NoSuperlatives("finest"));

        Assert.Equal(CheckErrorCodes.NameTaken, Assert.Single(second.Errors).Code);
        Assert.Single(await scenario.VersionRowsAsync());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_stores_the_next_version_from_the_one_it_read(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", NoSuperlatives());

        var revised = (await scenario.RunAsync(directory => directory.ReviseAsync("no-superlatives", 1, NoSuperlatives("finest")))).Value;
        var stale = await scenario.RunAsync(directory => directory.ReviseAsync("no-superlatives", 1, NoSuperlatives("greatest")));
        var unknown = await scenario.RunAsync(directory => directory.ReviseAsync("no-such-check", 1, NoSuperlatives()));

        Assert.Equal((2, 2), (revised.Version, revised.LatestVersion));
        Assert.Equal(CheckErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal(CheckErrorCodes.CheckNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(NoSuperlatives("finest").ToJson(), (await scenario.RunAsync(directory => directory.GetAsync("no-superlatives"))).Value.Definition.ToJson());
        Assert.Equal(NoSuperlatives().ToJson(), (await scenario.RunAsync(directory => directory.GetVersionAsync("no-superlatives", 1))).Value.Definition.ToJson());
        Assert.Equal(
            CheckErrorCodes.VersionNotFound,
            Assert.Single((await scenario.RunAsync(directory => directory.GetVersionAsync("no-superlatives", 3))).Errors).Code);

        var versions = await scenario.VersionRowsAsync();
        var revision = Assert.Single(await scenario.AuditRowsAsync(), row => row.Operation == "check.revised");
        Assert.Equal(versions[0].Id, revision.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_version_whose_examples_pass_becomes_the_one_that_runs(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        var created = (await scenario.CreateAsync("no-superlatives", NoSuperlatives())).Value;

        var activated = (await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1))).Value;
        var again = await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));

        Assert.Equal((1, (int?)1), (activated.Version, activated.ActiveVersion));
        Assert.Equal(CheckErrorCodes.AlreadyActive, Assert.Single(again.Errors).Code);
        var active = Assert.Single(await scenario.RunAsync(directory => directory.ListActiveAsync()));
        Assert.Equal(("no-superlatives", 1, (int?)1), (active.Name, active.Version, active.ActiveVersion));

        var activation = Assert.Single(await scenario.AuditRowsAsync(), row => row.Operation == "check.activated");
        Assert.Equal((created.Id, Amir.UserId, (Guid?)null), (activation.SubjectId, activation.InitiatorId, activation.PreviousVersionId));
    }

    // CHK-02: a check whose examples do not pass cannot be activated, and the failing example is named.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_version_with_a_failing_example_is_not_activated_and_the_example_is_named(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", NoSuperlatives("best", Example("says the best price", CheckVerdict.Pass, """{"headline": "The best price"}""")));

        var refused = await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));
        var missing = await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 2));

        var error = Assert.Single(refused.Errors);
        Assert.Equal(CheckErrorCodes.ExampleFailed, error.Code);
        Assert.Equal("Example 'says the best price' should pass, but the check failed it: Remove 'best' from headline.", error.Message);
        Assert.Equal(CheckErrorCodes.VersionNotFound, Assert.Single(missing.Errors).Code);
        Assert.Null((await scenario.RunAsync(directory => directory.GetAsync("no-superlatives"))).Value.ActiveVersion);
        Assert.Equal(["check.created"], (await scenario.AuditRowsAsync()).Select(row => row.Operation));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_keeps_the_active_version_until_the_next_one_is_activated(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", NoSuperlatives());
        await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));

        var revised = (await scenario.RunAsync(directory => directory.ReviseAsync("no-superlatives", 1, NoSuperlatives("finest")))).Value;
        var stillFirst = Assert.Single(await scenario.RunAsync(directory => directory.ListActiveAsync()));
        var second = (await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 2))).Value;
        var nowSecond = Assert.Single(await scenario.RunAsync(directory => directory.ListActiveAsync()));

        Assert.Equal((int?)1, revised.ActiveVersion);
        Assert.Equal((1, NoSuperlatives().ToJson()), (stillFirst.Version, stillFirst.Definition.ToJson()));
        Assert.Equal((2, (int?)2), (second.Version, second.ActiveVersion));
        Assert.Equal((2, (int?)2, 2), (nowSecond.Version, nowSecond.ActiveVersion, nowSecond.LatestVersion));

        var versions = await scenario.VersionRowsAsync();
        var activation = (await scenario.AuditRowsAsync()).Last();
        Assert.Equal(("check.activated", (Guid?)versions[0].Id), (activation.Operation, activation.PreviousVersionId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Deactivating_stops_the_check_from_running(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", NoSuperlatives());
        await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));

        var deactivated = (await scenario.RunAsync(directory => directory.DeactivateAsync("no-superlatives"))).Value;
        var again = await scenario.RunAsync(directory => directory.DeactivateAsync("no-superlatives"));

        Assert.Null(deactivated.ActiveVersion);
        Assert.Equal(CheckErrorCodes.NotActive, Assert.Single(again.Errors).Code);
        Assert.Empty(await scenario.RunAsync(directory => directory.ListActiveAsync()));
        Assert.Equal(
            [new CheckSummary(deactivated.Id, "no-superlatives", 1, null)],
            await scenario.RunAsync(directory => directory.ListAsync()));

        var versions = await scenario.VersionRowsAsync();
        var deactivation = (await scenario.AuditRowsAsync()).Last();
        Assert.Equal(("check.deactivated", (Guid?)versions[0].Id), (deactivation.Operation, deactivation.PreviousVersionId));
    }

    // D17: the rule author activates a check; an agent can propose and revise one, nothing more.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_creates_and_revises_a_check_but_cannot_activate_or_deactivate_it(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        scenario.Initiator = Agent;
        await scenario.CreateAsync("no-superlatives", NoSuperlatives());
        await scenario.RunAsync(directory => directory.ReviseAsync("no-superlatives", 1, NoSuperlatives("finest")));

        var activate = await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));
        scenario.Initiator = Amir;
        await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));
        scenario.Initiator = Agent;
        var deactivate = await scenario.RunAsync(directory => directory.DeactivateAsync("no-superlatives"));

        Assert.Equal(("check_people_only", "Only a person can activate a check; an agent cannot."), (activate.Errors[0].Code, activate.Errors[0].Message));
        Assert.Equal(CheckErrorCodes.PeopleOnly, Assert.Single(deactivate.Errors).Code);
        Assert.Equal((int?)1, Assert.Single(await scenario.RunAsync(directory => directory.ListActiveAsync())).ActiveVersion);
        Assert.Equal(
            [("check.created", Agent.CredentialId), ("check.revised", Agent.CredentialId), ("check.activated", Amir.UserId)],
            (await scenario.AuditRowsAsync()).Select(row => (row.Operation, row.InitiatorId)));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_example_of_a_content_type_runs_with_that_type_s_definition(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        var definition = new CheckDefinition(
            "sample.definedFields",
            "Pages hold only the fields their type defines.",
            null,
            [
                Example("defined fields", CheckVerdict.Pass, """{"headline": "A kettle"}""", "product-page"),
                Example("an undefined field", CheckVerdict.Fail, """{"tagline": "A kettle"}""", "product-page"),
            ]);
        await scenario.CreateAsync("defined-fields", definition);

        var beforeTheType = await scenario.RunAsync(directory => directory.ActivateAsync("defined-fields", 1));
        await scenario.CreateContentTypeAsync("product-page", SchemaScenario.Field("headline", "text"), SchemaScenario.Field("body", "richText"));
        var activated = await scenario.RunAsync(directory => directory.ActivateAsync("defined-fields", 1));

        Assert.Equal(
            "Example 'defined fields' is of the content type 'product-page', which does not exist in this site.",
            Assert.Single(beforeTheType.Errors).Message);
        Assert.True(activated.IsSuccess, string.Join("; ", activated.Errors.Select(error => error.Message)));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_activations_at_the_same_time_the_second_changes_nothing(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", NoSuperlatives());

        var lost = await scenario.RaceAsync(
            directory => directory.ActivateAsync("no-superlatives", 1),
            directory => directory.ActivateAsync("no-superlatives", 1));

        Assert.Equal(CheckErrorCodes.VersionConflict, Assert.Single(lost.Errors).Code);
        Assert.Single(await scenario.AuditRowsAsync(), row => row.Operation == "check.activated");
        Assert.Equal(1, await scenario.QueryAsync(context => context.Checks.Select(row => row.ActiveVersion).SingleAsync()));
    }
}
