using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.IntegrationTests.Media;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

// UX-04: the admin targets WCAG 2.1 AA, checked with axe-core on the showcase and the desk's pages in both directions and themes.
public sealed class AccessibilityTests
{
    private static readonly AxeRunOptions Wcag21AA = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
    };

    [BrowserFact]
    public async Task The_showcase_desk_and_set_view_have_no_wcag_21_aa_violations()
    {
        await using var session = await BrowserSession.StartAsync();
        var set = await AwaitingReviewAsync(session);
        var asset = await session.Host.AsUserAsync<IMediaLibrary, Guid>(session.OwnerId, async library => (await library.UploadAsync(new NewAsset(
            new MemoryStream(MediaFiles.Jpeg()),
            "image/jpeg",
            AssetMetadata.Create("drill.jpg", "products", ["hero"], null, null).Value))).Value.Id);
        await session.SignInWithKeyboardAsync();

        string[] paths = ["/ui", "/", "/?queue=stale-facts", $"/sets/{set.Id}?queue=awaiting-me", "/security", "/media", $"/media/{asset}", "/content", "/content/product", "/content/product/new", "/content/supplier/new", "/content/kitchen-sink/new"];
        var violations = new List<string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            await session.Page.Context.AddCookiesAsync([new Cookie { Name = "fluentcms-theme", Value = theme, Url = session.BaseUrl }]);
            foreach (var path in paths)
            {
                foreach (var direction in new[] { "ltr", "rtl" })
                {
                    var address = $"{session.BaseUrl}{path}{(path.Contains('?') ? '&' : '?')}dir={direction}";
                    await session.Page.GotoAsync(address);
                    await session.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    var result = await session.Page.RunAxe(Wcag21AA);
                    violations.AddRange(result.Violations.Select(violation =>
                        $"{theme} {path} {direction}: {violation.Id} ({violation.Impact}) {violation.Help}: "
                        + string.Join(" | ", violation.Nodes.Take(3).Select(node => node.Target + " " + node.Html))));
                }
            }
        }

        // A refused submit, with its problems shown next to their fields.
        await session.Page.GotoAsync($"{session.BaseUrl}/content/kitchen-sink/new");
        await session.Page.Locator("input[name='Form.Values[title]']").FillAsync("Drill");
        await session.Page.Locator("textarea[name='Form.Values[extra]']").FillAsync("{");
        await session.Page.Locator("textarea[name='Form.Rationale']").FillAsync("Add the drill.");
        await session.Page.Locator("[data-action=save]").ClickAsync();
        await Assertions.Expect(session.Page.Locator("[data-slot=form-message]")).ToHaveCountAsync(1);
        var refused = await session.Page.RunAxe(Wcag21AA);
        violations.AddRange(refused.Violations.Select(violation => $"refused form: {violation.Id} ({violation.Impact}) {violation.Help}: "
            + string.Join(" | ", violation.Nodes.Take(3).Select(node => node.Target + " " + node.Html))));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static System.Text.Json.JsonElement Json(string json)
    {
        return System.Text.Json.JsonDocument.Parse(json).RootElement;
    }

    private static async Task<ChangeSet> AwaitingReviewAsync(BrowserSession session)
    {
        var type = await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory =>
            (await directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory =>
            (await directory.CreateAsync(new NewContentType("Supplier", "supplier", new ContentTypeDefinition([new FieldDefinition("name", "text")], isEntity: true)), TestChangeSetTargets.ContentModel)).Value);
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory => (await directory.CreateAsync(new NewContentType(
            "Kitchen sink",
            "kitchen-sink",
            new ContentTypeDefinition(
            [
                new FieldDefinition("title", "text", required: true),
                new FieldDefinition("body", "richText"),
                new FieldDefinition("price", "number"),
                new FieldDefinition("active", "boolean"),
                new FieldDefinition("launch", "dateTime"),
                new FieldDefinition("day", "dateTime", settings: Json("""{"dateOnly": true}""")),
                new FieldDefinition("colour", "enum", settings: Json("""{"values": ["red", "blue"]}""")),
                new FieldDefinition("product", "reference", settings: Json("""{"target": "product"}""")),
                new FieldDefinition("hero", "media"),
                new FieldDefinition("tags", "list", settings: Json("""{"itemType": "text"}""")),
                new FieldDefinition("extra", "json"),
            ])), TestChangeSetTargets.ContentModel)).Value);

        return await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet("Add the product type.", ["https://example.com/spec"]))).Value;
            await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
            await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).Value;
        });
    }
}
