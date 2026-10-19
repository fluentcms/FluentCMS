using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Content;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Media;

public sealed class AltTextCheckTests
{
    private static readonly ContentTypeDefinition ProductPage = new(
    [
        new FieldDefinition("headline", "text"),
        new FieldDefinition("hero", "media"),
        new FieldDefinition("gallery", "list", settings: JsonSerializer.Deserialize<JsonElement>("""{"itemType": "media"}""")),
    ]);

    private static readonly ContentTypeDefinition Product = new([new FieldDefinition("photo", "media")], isEntity: true);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_media_field_naming_an_asset_without_alt_text_fails_and_names_the_field(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var bare = await UploadAsync(scenario, "drill.jpg", altText: null);
        var described = await UploadAsync(scenario, "saw.jpg", altText: "A circular saw on a bench.");

        var outcome = await EvaluateAsync(scenario, ProductPage, $$"""{"headline": "Drills", "hero": "{{bare.Id}}", "gallery": ["{{described.Id}}", "{{bare.Id}}"]}""");

        Assert.Equal(CheckVerdict.Fail, outcome.Verdict);
        Assert.Equal(["/hero", "/gallery/1"], outcome.Findings.Select(finding => finding.Pointer));
        Assert.Equal(
            "Add alt text to the image 'drill.jpg' in hero that says what it shows; set it with update_media.",
            outcome.Findings[0].Message);
        Assert.Equal(bare.Id.ToString(), outcome.Findings[0].Excerpt);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Alt_text_in_the_assets_latest_version_passes_on_pages_and_entities(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var asset = await UploadAsync(scenario, "drill.jpg", altText: null);
        var entity = $$"""{"photo": {"value": "{{asset.Id}}", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null} }""";

        var before = await EvaluateAsync(scenario, Product, entity);
        await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 1, AssetMetadata.Create("drill.jpg", null, [], "A cordless drill.", null).Value));
        var page = await EvaluateAsync(scenario, ProductPage, $$"""{"hero": "{{asset.Id}}"}""");
        var entityAfter = await EvaluateAsync(scenario, Product, entity);
        var unknown = await EvaluateAsync(scenario, ProductPage, $$"""{"hero": "{{Guid.NewGuid()}}"}""");

        Assert.Equal("/photo/value", Assert.Single(before.Findings).Pointer);
        Assert.Equal(
            [CheckVerdict.Pass, CheckVerdict.Pass, CheckVerdict.Pass],
            new[] { page, entityAfter, unknown }.Select(outcome => outcome.Verdict));
    }

    private static async Task<MediaAsset> UploadAsync(EntryScenario scenario, string fileName, string? altText)
    {
        var metadata = AssetMetadata.Create(fileName, null, [], altText, null).Value;
        return (await LibraryAsync(scenario, library => library.UploadAsync(new NewAsset(new MemoryStream([1, 2, 3]), "image/jpeg", metadata)))).Value;
    }

    private static async Task<CheckOutcome> EvaluateAsync(EntryScenario scenario, ContentTypeDefinition definition, string fields)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var subject = new CheckSubject(JsonSerializer.Deserialize<JsonElement>(fields), [], "product-page", definition);
        var input = new CheckInput(JsonSerializer.Deserialize<JsonElement>("{}"), subject, scope.ServiceProvider);

        return await scope.ServiceProvider.GetRequiredService<CheckTypeRegistry>().EvaluateAsync("altText", input);
    }

    private static async Task<T> LibraryAsync<T>(EntryScenario scenario, Func<IMediaLibrary, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMediaLibrary>());
    }
}
