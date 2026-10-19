using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;
using static FluentCMS.IntegrationTests.ChangeSets.EntryChangeSetTests;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Delivery;

public sealed class PreviewTests
{
    // S1 with API-04: a frontend previews the new price on the page before anyone approves the set, and the token ends with it.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_token_shows_the_set_as_it_would_publish_until_the_set_publishes(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await LiveCatalogAsync(scenario);
        var set = await scenario.OpenAsync(
            Operation(OperationKind.Update, ItemKind.Entity, product, 1, 2),
            Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));
        var token = (await TokensAsync(scenario, tokens => tokens.IssueAsync(set.Id))).Value;

        var previewed = (await PreviewAsync(scenario, preview => preview.GetAsync(token.Token, page))).Value;
        var changes = (await PreviewAsync(scenario, preview => preview.ChangesAsync(token.Token))).Value;
        var published = (await PublishedAsync(scenario, content => content.GetAsync(page))).Value;

        Assert.Equal(2, previewed.Version);
        AssertSameFields("""{"title": "Widget, now cheaper", "price": 24.99}""", previewed.Fields);
        Assert.Equal(set.Id, changes.ChangeSetId);
        Assert.Equal([(product, 2), (page, 2)], changes.Items.Select(item => (item.Id, item.Version)));
        AssertSameFields("""{"title": "Widget", "price": 19.99}""", published.Fields);

        await scenario.PublishAsync(set.Id);

        Assert.Equal(DeliveryErrorCodes.PreviewTokenInvalid, Code(await PreviewAsync(scenario, preview => preview.GetAsync(token.Token, page))));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_token_stops_working_when_revoked_when_its_set_is_rejected_and_when_it_expires(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await LiveCatalogAsync(scenario);
        var set = await scenario.OpenAsync(Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));
        var revoked = (await TokensAsync(scenario, tokens => tokens.IssueAsync(set.Id))).Value;
        var expiring = (await TokensAsync(scenario, tokens => tokens.IssueAsync(set.Id))).Value;
        var unused = (await TokensAsync(scenario, tokens => tokens.IssueAsync(set.Id))).Value;

        Assert.True((await TokensAsync(scenario, tokens => tokens.RevokeAsync(revoked.Id))).IsSuccess);
        Assert.Equal(DeliveryErrorCodes.PreviewTokenInvalid, Code(await PreviewAsync(scenario, preview => preview.GetAsync(revoked.Token, page))));
        Assert.True((await PreviewAsync(scenario, preview => preview.GetAsync(expiring.Token, page))).IsSuccess);
        Assert.Equal(DeliveryErrorCodes.PreviewTokenInvalid, Code(await PreviewAsync(scenario, preview => preview.GetAsync(PreviewTokenKeys.Generate(), page))));

        scenario.AdvanceClock(TimeSpan.FromDays(7));
        Assert.Equal(DeliveryErrorCodes.PreviewTokenInvalid, Code(await PreviewAsync(scenario, preview => preview.GetAsync(expiring.Token, page))));

        var second = await scenario.OpenAsync(Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));
        var onRejected = (await TokensAsync(scenario, tokens => tokens.IssueAsync(second.Id))).Value;
        Assert.True((await PreviewAsync(scenario, preview => preview.ChangesAsync(onRejected.Token))).IsSuccess);
        await scenario.RunAsync(directory => directory.TransitionAsync(second.Id, ChangeSetState.ChecksRunning));
        await scenario.AsPersonAsync(directory => directory.TransitionAsync(second.Id, ChangeSetState.AwaitingReview));
        Assert.True((await scenario.AsPersonAsync(directory => directory.RejectAsync(second.Id, "The headline is off."))).IsSuccess);

        Assert.Equal(DeliveryErrorCodes.PreviewTokenInvalid, Code(await PreviewAsync(scenario, preview => preview.ChangesAsync(onRejected.Token))));
        Assert.Equal(DeliveryErrorCodes.PreviewTokenInvalid, Code(await PreviewAsync(scenario, preview => preview.ChangesAsync("not a token"))));

        var listed = (await TokensAsync(scenario, tokens => tokens.ListAsync(set.Id))).Value;
        Assert.Equal([unused.Id, expiring.Id, revoked.Id], listed.Select(token => token.Id));
        Assert.NotNull(listed[^1].RevokedAt);
        var events = await scenario.QueryAsync(context => context.AuditEvents.Where(row => row.SubjectType == "preview_token").ToListAsync());
        Assert.Equal(4, events.Count(row => row.Operation == "preview_token.issued"));
        var revokedEvent = Assert.Single(events, row => row.Operation == "preview_token.revoked");
        Assert.Equal((revoked.Id, set.Id), (revokedEvent.SubjectId!.Value, revokedEvent.ChangeSetId!.Value));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_a_person_issues_a_token_and_only_for_a_set_that_can_still_publish(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, _) = await LiveCatalogAsync(scenario);
        var published = await scenario.PublishAsync(Operation(OperationKind.Update, ItemKind.Entry, page, 1, 2));
        var open = await scenario.OpenAsync();
        var token = (await TokensAsync(scenario, tokens => tokens.IssueAsync(open.Id))).Value;

        var ended = await TokensAsync(scenario, tokens => tokens.IssueAsync(published.Id));
        var unknown = await TokensAsync(scenario, tokens => tokens.IssueAsync(Guid.CreateVersion7()));
        var unknownRevoke = await TokensAsync(scenario, tokens => tokens.RevokeAsync(Guid.CreateVersion7()));
        scenario.Initiator = Agent;
        var byAgent = await TokensAsync(scenario, tokens => tokens.IssueAsync(open.Id));
        var revokeByAgent = await TokensAsync(scenario, tokens => tokens.RevokeAsync(token.Id));

        Assert.Equal(DeliveryErrorCodes.ChangeSetNotPreviewable, Code(ended));
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Code(unknown));
        Assert.Equal(DeliveryErrorCodes.PreviewTokenNotFound, Assert.Single(unknownRevoke.Errors).Code);
        Assert.Equal(DeliveryErrorCodes.PreviewByPeopleOnly, Code(byAgent));
        Assert.Equal(DeliveryErrorCodes.PreviewByPeopleOnly, Assert.Single(revokeByAgent.Errors).Code);
        Assert.StartsWith(PreviewTokenKeys.Prefix, token.Token, StringComparison.Ordinal);
        Assert.Equal(1, await scenario.QueryAsync(context => context.PreviewTokens.CountAsync()));
        Assert.Equal(
            PreviewTokenKeys.Hash(token.Token),
            await scenario.QueryAsync(context => context.PreviewTokens.Select(row => row.TokenHash).SingleAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task What_the_set_unpublishes_is_gone_and_what_it_leaves_reads_as_published(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var (page, product) = await LiveCatalogAsync(scenario);
        var draft = (await EntriesAsync(scenario, entries => entries.CreateAsync(new NewEntry("page", Json("""{"title": "Draft"}"""))))).Value.Id;
        var set = await scenario.OpenAsync(Operation(OperationKind.Delete, ItemKind.Entity, product, 1, null));
        var token = (await TokensAsync(scenario, tokens => tokens.IssueAsync(set.Id))).Value;

        var shown = (await PreviewAsync(scenario, preview => preview.GetAsync(token.Token, page))).Value;
        var unpublished = await PreviewAsync(scenario, preview => preview.GetAsync(token.Token, product));
        var hidden = await PreviewAsync(scenario, preview => preview.GetAsync(token.Token, draft));
        var changes = (await PreviewAsync(scenario, preview => preview.ChangesAsync(token.Token))).Value;

        Assert.Equal(1, shown.Version);
        AssertSameFields("""{"title": "Widget", "price": null}""", shown.Fields);
        Assert.Equal(UnresolvedReason.NotPublished, Assert.Single(shown.Unresolved).Reason);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Code(unpublished));
        Assert.Equal(ContentErrorCodes.EntryNotFound, Code(hidden));
        Assert.Empty(changes.Items);
    }

    // The catalog published at version 1, with version 2 of both stored: the product at 24.99 and the page with a new title.
    private static async Task<(Guid Page, Guid Product)> LiveCatalogAsync(ChangeSetScenario scenario)
    {
        var (page, product) = await CatalogAsync(scenario);
        await scenario.PublishAsync(
            Operation(OperationKind.Create, ItemKind.Entity, product, null, 1),
            Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));
        Assert.True((await EntriesAsync(scenario, entries => entries.UpdateAsync(product, 1, Json(ProductFields("24.99"))))).IsSuccess);
        await UpdatePageAsync(scenario, page, 1, "Widget, now cheaper");
        return (page, product);
    }

    private static ChangeOperation Operation(OperationKind kind, ItemKind itemKind, Guid item, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, itemKind, item, baseVersion, proposedVersion).Value;
    }

    private static string Code<T>(Result<T> result)
    {
        return Assert.Single(result.Errors).Code;
    }

    private static async Task<T> TokensAsync<T>(ChangeSetScenario scenario, Func<IPreviewTokens, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPreviewTokens>());
    }

    private static async Task<T> PreviewAsync<T>(ChangeSetScenario scenario, Func<IPreviewContent, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPreviewContent>());
    }

    private static async Task<T> PublishedAsync<T>(ChangeSetScenario scenario, Func<IPublishedContent, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPublishedContent>());
    }
}
