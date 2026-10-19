using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
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
        var bare = await PublishedAsync(scenario, await UploadAsync(scenario, "drill.jpg", altText: null));
        var described = await PublishedAsync(scenario, await UploadAsync(scenario, "saw.jpg", altText: "A circular saw on a bench."));

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
    public async Task Alt_text_of_the_published_version_passes_on_pages_and_entities(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var asset = await PublishedAsync(scenario, await UploadAsync(scenario, "drill.jpg", altText: null));
        var entity = $$"""{"photo": {"value": "{{asset.Id}}", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null} }""";

        var before = await EvaluateAsync(scenario, Product, entity);
        await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 1, AssetMetadata.Create("drill.jpg", null, [], "A cordless drill.", null).Value));
        await PublishedAsync(scenario, asset, 2);
        var page = await EvaluateAsync(scenario, ProductPage, $$"""{"hero": "{{asset.Id}}"}""");
        var entityAfter = await EvaluateAsync(scenario, Product, entity);
        var unknown = await EvaluateAsync(scenario, ProductPage, $$"""{"hero": "{{Guid.NewGuid()}}"}""");

        Assert.Equal("/photo/value", Assert.Single(before.Findings).Pointer);
        Assert.Equal(
            [CheckVerdict.Pass, CheckVerdict.Pass, CheckVerdict.Pass],
            new[] { page, entityAfter, unknown }.Select(outcome => outcome.Verdict));
    }

    // An image no set publishes with this content can go live later in any version, even one a rejected set described, so the
    // content fails until the image is published or goes live with it.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_image_neither_published_nor_in_the_content_s_set_fails_whatever_its_latest_version_says(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var asset = await UploadAsync(scenario, "drill.jpg", altText: null);
        await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 1, AssetMetadata.Create("drill.jpg", null, [], "A cordless drill.", null).Value));
        var page = $$"""{"hero": "{{asset.Id}}"}""";

        await using var scope = scenario.Services.CreateAsyncScope();
        var sets = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
        var other = (await sets.OpenAsync(new NewChangeSet("Something else.", []))).Value;
        var holding = (await sets.OpenAsync(new NewChangeSet("Launch the drill.", []))).Value;
        Assert.True((await sets.AddOperationAsync(holding.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Media, asset.Id, null, 2).Value)).IsSuccess);

        var alone = await EvaluateAsync(scenario, ProductPage, page);
        var withOther = await EvaluateAsync(scenario, ProductPage, page, other.Id);
        var withHolding = await EvaluateAsync(scenario, ProductPage, page, holding.Id);

        var finding = Assert.Single(alone.Findings);
        Assert.Equal(
            "The image 'drill.jpg' in hero is not published; give it alt text with update_media in the change set this content goes into, so both go live together.",
            finding.Message);
        Assert.Equal(CheckVerdict.Fail, withOther.Verdict);
        Assert.Equal(CheckVerdict.Pass, withHolding.Verdict);
    }

    // MED-02: what goes live counts. A published image without alt text fails while a newer version with alt text waits in a set,
    // and passes when the page goes live with that set.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_published_version_counts_unless_the_pages_change_set_proposes_another(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var asset = await UploadAsync(scenario, "drill.jpg", altText: null);
        await scenario.Schema.QueryAsync(context => context.MediaAssets
            .Where(row => row.Id == asset.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 1, AssetMetadata.Create("drill.jpg", null, [], "A cordless drill.", null).Value));
        var page = $$"""{"hero": "{{asset.Id}}"}""";

        await using var scope = scenario.Services.CreateAsyncScope();
        var sets = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
        var set = (await sets.OpenAsync(new NewChangeSet("Describe the drill.", []))).Value;
        Assert.True((await sets.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Update, ItemKind.Media, asset.Id, 1, 2).Value)).IsSuccess);
        var other = (await sets.OpenAsync(new NewChangeSet("Something else.", []))).Value;

        Assert.Equal(CheckVerdict.Fail, (await EvaluateAsync(scenario, ProductPage, page)).Verdict);
        Assert.Equal(CheckVerdict.Fail, (await EvaluateAsync(scenario, ProductPage, page, other.Id)).Verdict);
        Assert.Equal(CheckVerdict.Pass, (await EvaluateAsync(scenario, ProductPage, page, set.Id)).Verdict);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Media_that_is_not_an_image_needs_no_alt_text(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var metadata = AssetMetadata.Create("manual.pdf", null, [], null, null).Value;
        var manual = (await LibraryAsync(scenario, library => library.UploadAsync(new NewAsset(new MemoryStream("%PDF-1.7"u8.ToArray()), "application/pdf", metadata)))).Value;

        Assert.Equal(CheckVerdict.Pass, (await EvaluateAsync(scenario, ProductPage, $$"""{"hero": "{{manual.Id}}"}""")).Verdict);
    }

    private static async Task<MediaAsset> PublishedAsync(EntryScenario scenario, MediaAsset asset, int version = 1)
    {
        await scenario.Schema.QueryAsync(context => context.MediaAssets
            .Where(row => row.Id == asset.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, version)));
        return asset;
    }

    private static async Task<MediaAsset> UploadAsync(EntryScenario scenario, string fileName, string? altText)
    {
        var metadata = AssetMetadata.Create(fileName, null, [], altText, null).Value;
        return (await LibraryAsync(scenario, library => library.UploadAsync(new NewAsset(new MemoryStream(MediaFiles.Jpeg()), "image/jpeg", metadata)))).Value;
    }

    private static async Task<CheckOutcome> EvaluateAsync(EntryScenario scenario, ContentTypeDefinition definition, string fields, Guid? changeSetId = null)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var subject = new CheckSubject(JsonSerializer.Deserialize<JsonElement>(fields), [], "product-page", definition, ChangeSetId: changeSetId);
        var input = new CheckInput(JsonSerializer.Deserialize<JsonElement>("{}"), subject, scope.ServiceProvider);

        return await scope.ServiceProvider.GetRequiredService<CheckTypeRegistry>().EvaluateAsync("altText", input);
    }

    private static async Task<T> LibraryAsync<T>(EntryScenario scenario, Func<IMediaLibrary, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMediaLibrary>());
    }
}
