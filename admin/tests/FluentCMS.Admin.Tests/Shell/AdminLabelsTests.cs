using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Shell;

// The labels of spec 4.1 and AQ10: what the schema says first, then what the admin derives.
public sealed class AdminLabelsTests
{
    private static readonly Guid Id = new("0193a1b2-0000-7000-8000-0000000000e1");

    [Fact]
    public void ATypeIsLabelledByTheAppsResourceFirst()
    {
        var type = Type("site-settings", displayName: "Settings of the site");

        AdminLabels.Type(type, new Resources(new() { ["ContentType.site-settings"] = "Réglages" })).ShouldBe("Réglages");
    }

    [Fact]
    public void WithoutAResourceATypeIsLabelledByItsDisplayName() =>
        AdminLabels.Type(Type("site-settings", displayName: "Settings of the site"), new Resources([])).ShouldBe("Settings of the site");

    [Theory]
    [InlineData("site-settings", "Site settings")]
    [InlineData("article", "Article")]
    [InlineData("blog-post-category", "Blog post category")]
    public void WithoutEitherATypeIsLabelledByItsNameMadeReadable(string name, string label) =>
        AdminLabels.Type(Type(name, displayName: " "), new Resources([])).ShouldBe(label);

    [Fact]
    public void AnEntryIsLabelledByTheTitleFieldOfItsTypeFirst()
    {
        var type = Type("article", titleField: "headline", fields: [Text("title"), Text("headline")]);

        AdminLabels.Entry(type, Entry(new() { ["title"] = "A title", ["headline"] = "A headline" })).ShouldBe("A headline");
    }

    [Fact]
    public void ThenByATextFieldNamedTitleBeforeOneNamedName()
    {
        var type = Type("author", fields: [Text("name"), Text("title")]);

        AdminLabels.Entry(type, Entry(new() { ["name"] = "Ada", ["title"] = "Dr" })).ShouldBe("Dr");
        AdminLabels.Entry(type, Entry(new() { ["name"] = "Ada" })).ShouldBe("Ada");
    }

    [Fact]
    public void AFieldNamedTitleThatIsNoTextFieldIsPassedOver()
    {
        var type = Type("event", fields: [Field("title", "richText"), Text("caption", required: true)]);

        AdminLabels.Entry(type, Entry(new() { ["title"] = "<p>x</p>", ["caption"] = "Opening" })).ShouldBe("Opening");
    }

    [Fact]
    public void ThenByTheSourceFieldOfTheSlug()
    {
        var type = Type("page", fields: [Text("summary", required: true), Text("heading"), Field("slug", "slug", settings: new JsonObject { ["source"] = "heading" })]);

        AdminLabels.Entry(type, Entry(new() { ["summary"] = "Short", ["heading"] = "About us" })).ShouldBe("About us");
    }

    [Fact]
    public void ThenByTheFirstRequiredTextField()
    {
        var type = Type("quote", fields: [Text("note"), Text("text", required: true), Text("source", required: true)]);

        AdminLabels.Entry(type, Entry(new() { ["note"] = "n", ["text"] = "To be", ["source"] = "Hamlet" })).ShouldBe("To be");
    }

    [Fact]
    public void ThenByTheId()
    {
        var type = Type("image", titleField: "caption", fields: [Text("caption"), Field("count", "integer")]);

        AdminLabels.Entry(type, Entry(new() { ["caption"] = "  ", ["count"] = 3 })).ShouldBe(Id.ToString("D"));
    }

    // A read leaves a hidden field out, so the label falls back past a hidden titleField to the next candidate.
    [Fact]
    public void TheLabelFieldIsTheFirstCandidateThatIsNotHidden()
    {
        var type = Type("author", titleField: "secret", fields: [Text("secret") with { Hidden = true }, Text("name")]);

        AdminLabels.LabelField(type).ShouldBe("name");
        AdminLabels.LabelField(Type("author", fields: [Text("name") with { Hidden = true }])).ShouldBeNull();
    }

    private static SchemaTypeDescription Type(string name, string? displayName = null, string? titleField = null, SchemaFieldDescription[]? fields = null) => new()
    {
        Name = name,
        Kind = "collection",
        DisplayName = displayName,
        TitleField = titleField,
        Fields = fields ?? [],
    };

    private static SchemaFieldDescription Text(string name, bool required = false) => Field(name, "text", required);

    private static SchemaFieldDescription Field(string name, string type, bool required = false, JsonObject? settings = null) => new()
    {
        Name = name,
        Type = type,
        Validation = new FieldValidationDescription { Required = required },
        Settings = settings ?? [],
    };

    private static ContentEntry Entry(Dictionary<string, JsonNode?> fields) => new() { Id = Id, Fields = new JsonObject(fields) };

    private sealed class Resources(Dictionary<string, string> values) : IStringLocalizer
    {
        public LocalizedString this[string name] =>
            values.TryGetValue(name, out var value) ? new LocalizedString(name, value) : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => values.Select(pair => new LocalizedString(pair.Key, pair.Value));
    }
}
