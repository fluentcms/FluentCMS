using System.Text.Json.Nodes;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using static FluentCMS.IntegrationTests.Schema.SchemaScenario;

namespace FluentCMS.IntegrationTests.Schema;

// The schema document over stored types (SCH-06): what an agent reads before it writes an entry.
public sealed class SchemaDocumentsTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_type_of_the_reference_model_has_a_document_its_example_entry_satisfies(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateReferenceModelAsync();

        foreach (var (slug, isEntity, _) in ReferenceModel)
        {
            var document = (await DocumentsAsync(scenario, documents => documents.GetTypeAsync(slug))).Value;
            var example = document["examples"]![0];

            Assert.Equal(isEntity, (bool)document["x-fluentcms"]!["entity"]!);
            Assert.Empty(await ErrorsAsync(document, example));
        }

        var product = (await DocumentsAsync(scenario, documents => documents.GetTypeAsync("product"))).Value;
        Assert.StartsWith("The list price, before discounts. A number from 0 to 99999.99.", (string?)product["properties"]!["price"]!["description"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_site_document_lists_every_stored_type(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateReferenceModelAsync();

        var site = await DocumentsAsync(scenario, documents => documents.GetSiteAsync());

        Assert.Equal(
            ReferenceModel.Select(type => type.Slug).Order(StringComparer.Ordinal),
            site["$defs"]!.AsObject().Select(definition => definition.Key));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_shows_in_the_document_and_an_unknown_slug_is_not_found(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateAsync("page", Field("title", "text"));
        await scenario.ReviseAsync("page", 1, new ContentTypeDefinition([Field("title", "text", description: "Shown in the browser tab.")]));

        var page = (await DocumentsAsync(scenario, documents => documents.GetTypeAsync("page"))).Value;
        var missing = await DocumentsAsync(scenario, documents => documents.GetTypeAsync("nothing"));

        Assert.Equal("urn:fluentcms:content-type:page:2", (string?)page["$id"]);
        Assert.Equal("Shown in the browser tab. Plain text.", (string?)page["properties"]!["title"]!["description"]);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Assert.Single(missing.Errors).Code);
    }

    private static async Task<T> DocumentsAsync<T>(SchemaScenario scenario, Func<ISchemaDocuments, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISchemaDocuments>());
    }

    // An independent validator, so the test does not grade the document with the logic that wrote it.
    private static async Task<IReadOnlyList<string>> ErrorsAsync(JsonNode document, JsonNode? instance)
    {
        var schema = await JsonSchema.FromJsonAsync(document.ToJsonString());
        return schema.Validate(instance?.ToJsonString() ?? "null").Select(error => error.ToString()).ToList();
    }
}
