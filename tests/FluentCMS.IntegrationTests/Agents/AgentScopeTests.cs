using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using static FluentCMS.Core.Agents.AgentOperation;
using static FluentCMS.IntegrationTests.Agents.ScopeScenario;

namespace FluentCMS.IntegrationTests.Agents;

public sealed class AgentScopeTests
{
    private static readonly AgentOperation[] Every = [Read, Create, Update, Delete];

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_type_outside_the_scope_is_refused_and_left_out_of_every_list(string provider)
    {
        await using var scenario = await ScopeScenario.CreateAsync(provider);
        var product = scenario.Product.Id;
        var agent = await scenario.ActAsAgentAsync(["category"], null, Every);
        var counts = await scenario.CountsAsync();

        Result[] refused =
        [
            Plain(await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.GetAsync("product"))),
            Plain(await scenario.RunAsync<ISchemaDocuments, Result<JsonObject>>(documents => documents.GetTypeAsync("product"))),
            Plain(await scenario.RunAsync<IEntryDirectory, Result<IReadOnlyList<EntrySummary>>>(directory => directory.ListAsync("product"))),
            Plain(await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.GetAsync(product))),
            Plain(await scenario.RunAsync<IEntryDirectory, Result<EntryVersion>>(directory => directory.GetVersionAsync(product, 1))),
            Plain(await scenario.RunAsync<IEntryDirectory, Result<IReadOnlyList<EntryVersionSummary>>>(directory => directory.HistoryAsync(product))),
            Plain(await scenario.RunAsync<IResolvedEntries, Result<ResolvedEntry>>(entries => entries.ResolveAsync(product))),
            Plain(await scenario.RunAsync<IWhereUsed, Result<IReadOnlyList<EntryUsage>>>(whereUsed => whereUsed.ForEntryAsync(product))),
            Plain(await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.CreateAsync(new NewEntry("product", Json("""{"price":5}"""))))),
            Plain(await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(product, 1, Json("""{"price":5}""")))),
            await scenario.RunAsync<IEntryDirectory, Result>(directory => directory.DeleteAsync(product, 1)),
        ];

        Assert.All(refused, result =>
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, error.Code);
            Assert.Contains("'product' is not among the content types in its scope, which are 'category'", error.Message, StringComparison.Ordinal);
            Assert.Contains(agent.DisplayName, error.Message, StringComparison.Ordinal);
        });
        Assert.Equal(counts, await scenario.CountsAsync());

        var types = await scenario.RunAsync<IContentTypeDirectory, IReadOnlyList<ContentTypeSummary>>(directory => directory.ListAsync());
        var site = await scenario.RunAsync<ISchemaDocuments, JsonObject>(documents => documents.GetSiteAsync());
        Assert.Equal(["category"], types.Select(type => type.Slug));
        Assert.Equal(["category"], site["$defs"]!.AsObject().Select(definition => definition.Key));
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<IReadOnlyList<EntrySummary>>>(directory => directory.ListAsync("category"))).IsSuccess);

        // Deleted entries of a type outside the scope are left out as well.
        scenario.Initiator = Person;
        await scenario.RunAsync<IEntryDirectory, Result>(directory => directory.DeleteAsync(product, 1));
        await scenario.RunAsync<IEntryDirectory, Result>(directory => directory.DeleteAsync(scenario.Category.Id, 1));
        scenario.Initiator = agent.Initiator;
        var deleted = await scenario.RunAsync<IEntryDirectory, IReadOnlyList<DeletedEntry>>(directory => directory.ListDeletedAsync());
        Assert.Equal([scenario.Category.Id], deleted.Select(entry => entry.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Each_operation_needs_its_own_grant(string provider)
    {
        await using var scenario = await ScopeScenario.CreateAsync(provider);
        var category = scenario.Category.Id;
        await scenario.ActAsAgentAsync(null, null, Read);
        var counts = await scenario.CountsAsync();

        (string Operation, Result Result)[] refused =
        [
            ("create", Plain(await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.CreateAsync(new NewEntry("category", Json("""{"name":"Garden"}""")))))),
            ("update", Plain(await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(category, 1, Json("""{"name":"Garden"}"""))))),
            ("delete", await scenario.RunAsync<IEntryDirectory, Result>(directory => directory.DeleteAsync(category, 1))),
            ("delete", Plain(await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.RestoreAsync(category)))),
            ("create", Plain(await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.CreateAsync(
                new NewContentType("Region", "region", new ContentTypeDefinition([new FieldDefinition("name", "text")])), TestChangeSetTargets.ContentModel)))),
            ("update", Plain(await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.ReviseAsync(
                "category", 1, new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("slug", "text")]), TestChangeSetTargets.ContentModel)))),
        ];

        Assert.All(refused, refusal =>
        {
            var error = Assert.Single(refusal.Result.Errors);
            Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, error.Code);
            Assert.Contains($"may not {refusal.Operation} ", error.Message, StringComparison.Ordinal);
            Assert.Contains("its scope allows read only", error.Message, StringComparison.Ordinal);
        });
        Assert.Equal(counts, await scenario.CountsAsync());
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.GetAsync(category))).IsSuccess);

        // An agent that may not read sees no types, and its writes need read too: they are checked against the definition they read.
        await scenario.ActAsAgentAsync(null, null, Create);
        Assert.Empty(await scenario.RunAsync<IContentTypeDirectory, IReadOnlyList<ContentTypeSummary>>(directory => directory.ListAsync()));
        var writeWithoutRead = await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.CreateAsync(new NewEntry("category", Json("""{"name":"Garden"}"""))));
        Assert.Contains("may not read the content type 'category' or its entries", Assert.Single(writeWithoutRead.Errors).Message, StringComparison.Ordinal);

        await scenario.ActAsAgentAsync(null, null, Every);
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.CreateAsync(new NewEntry("category", Json("""{"name":"Garden"}"""))))).IsSuccess);
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(category, 1, Json("""{"name":"Home"}""")))).IsSuccess);
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result>(directory => directory.DeleteAsync(category, 2))).IsSuccess);
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.RestoreAsync(category))).IsSuccess);
        Assert.True((await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.ReviseAsync(
            "category", 1, new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("slug", "text")]), TestChangeSetTargets.ContentModel))).IsSuccess);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_field_list_narrows_writes_of_its_type_to_the_listed_fields(string provider)
    {
        await using var scenario = await ScopeScenario.CreateAsync(provider);
        var product = scenario.Product.Id;
        var productType = (await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.GetAsync("product"))).Value.Id;
        await scenario.ActAsAgentAsync(null, ["product.price"], Every);

        // Sending a field back unchanged is not a change, so only the price counts here.
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(product, 1, Json("""{"name":"Kettle","price":25}""")))).IsSuccess);
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.CreateAsync(new NewEntry("product", Json("""{"price":5}"""))))).IsSuccess);
        Assert.True((await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(scenario.Category.Id, 1, Json("""{"name":"Home"}""")))).IsSuccess);
        Assert.True((await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.ReviseAsync(
            "category", 1, new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("slug", "text")]), TestChangeSetTargets.ContentModel))).IsSuccess);
        var counts = await scenario.CountsAsync();

        var renamed = await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(product, 2, Json("""{"name":"Teapot","price":25}""")));
        var dropped = await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.UpdateAsync(product, 2, Json("""{"price":25}""")));
        var named = await scenario.RunAsync<IEntryDirectory, Result<Entry>>(directory => directory.CreateAsync(new NewEntry("product", Json("""{"name":"Mug","price":5}"""))));
        var definition = await scenario.RunAsync<IContentTypeDirectory, Result<ContentType>>(directory => directory.ReviseAsync(
            "product", 1, new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("price", "number", required: true)]), TestChangeSetTargets.ContentModel));
        var proposed = await scenario.RunAsync<IChangeSetDirectory, Result<ChangeSet>>(async directory =>
        {
            var changeSet = (await directory.OpenAsync(new NewChangeSet("Make the price required.", []))).Value;
            return await directory.AddOperationAsync(changeSet.Id, ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, productType, 1, 2).Value);
        });

        foreach (var write in new[] { Plain(renamed), Plain(dropped), Plain(named) })
        {
            var error = Assert.Single(write.Errors);
            Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, error.Code);
            Assert.Contains("may not change the field 'name' of 'product': its scope allows only the field 'price'", error.Message, StringComparison.Ordinal);
        }

        foreach (var write in new[] { Plain(definition), Plain(proposed) })
        {
            var error = Assert.Single(write.Errors);
            Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, error.Code);
            Assert.Contains("the definition of 'product': its scope limits 'product' to the field 'price'", error.Message, StringComparison.Ordinal);
        }

        // The change set itself was opened before the refused operation; nothing else was written.
        var after = await scenario.CountsAsync();
        Assert.Equal((counts.Entries, counts.Versions), (after.Entries, after.Versions));
        Assert.Equal(counts.Events + 1, after.Events);
    }

    private static Result Plain<T>(Result<T> result)
    {
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Errors);
    }
}
