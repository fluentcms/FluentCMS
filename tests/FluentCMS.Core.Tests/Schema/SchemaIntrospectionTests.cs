using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

// What the Blog sample cannot show: relations inside components and blocks, parts that nothing uses, and conditions and
// slug sources that read a field that went.
public class SchemaIntrospectionTests
{
    private static readonly SchemaModel Model = new SchemaBuilder().AddSchemaJson("""
        {
          "collections": [
            { "name": "page", "fields": [ { "name": "title", "type": "text" }, { "name": "card", "type": "component", "component": "card" }, { "name": "body", "type": "blocks", "blocks": ["teaser"] } ] },
            { "name": "secret", "fields": [ { "name": "name", "type": "text" } ] }
          ],
          "components": [
            { "name": "card", "fields": [ { "name": "label", "type": "text" }, { "name": "link", "type": "relation", "target": "secret" } ] },
            { "name": "spare", "fields": [ { "name": "note", "type": "text" } ] }
          ],
          "blocks": [ { "name": "teaser", "fields": [ { "name": "heading", "type": "text" }, { "name": "pick", "type": "relation", "target": "secret" } ] } ]
        }
        """).Build();

    private static readonly SchemaModel Conditional = new SchemaBuilder().AddSchemaJson("""
        {
          "collections": [
            {
              "name": "page",
              "localized": true,
              "fields": [
                { "name": "title", "type": "text" },
                { "name": "summary", "type": "text" },
                { "name": "slug", "type": "slug", "source": "title", "unique": true, "display": { "label": "Slug" } },
                {
                  "name": "intro",
                  "type": "text",
                  "visibleWhen": { "field": "title", "isEmpty": true },
                  "requiredWhen": { "field": "summary", "equals": "long" }
                },
                {
                  "name": "extra",
                  "type": "text",
                  "required": true,
                  "unique": true,
                  "indexed": true,
                  "searchable": true,
                  "readOnly": true,
                  "hidden": true,
                  "localizable": true,
                  "formerNames": ["more"],
                  "defaultValue": "x",
                  "minLength": 1,
                  "maxLength": 9,
                  "pattern": "^[a-z]+$",
                  "display": { "label": "Extra", "help": "Help", "placeholder": "x", "group": "Main", "tab": "Info", "width": 6 },
                  "visibleWhen": { "or": [ { "field": "summary", "equals": "long" }, { "not": { "field": "title", "isEmpty": true } } ] },
                  "requiredWhen": { "and": [ { "field": "summary", "in": ["a", "b"] }, { "not": { "field": "summary", "isEmpty": true } } ] }
                },
                { "name": "rank", "type": "decimal", "minimum": 0, "maximum": 5.5, "precision": 4, "scale": 1, "visibleWhen": { "field": "title", "isEmpty": true } },
                { "name": "related", "type": "relation", "target": "page", "multiple": true, "ordered": true, "reverse": "relatedTo", "visibleWhen": { "field": "title", "isEmpty": true } },
                { "name": "body", "type": "blocks", "blocks": ["teaser"] }
              ]
            },
            { "name": "secret", "fields": [ { "name": "name", "type": "text" } ] }
          ],
          "components": [
            {
              "name": "card",
              "fields": [
                { "name": "label", "type": "text" },
                { "name": "link", "type": "relation", "target": "secret" },
                { "name": "hint", "type": "text", "visibleWhen": { "field": "link", "isEmpty": true }, "requiredWhen": { "field": "label", "isEmpty": true } }
              ]
            }
          ],
          "blocks": [
            {
              "name": "teaser",
              "fields": [
                { "name": "heading", "type": "text" },
                { "name": "pick", "type": "relation", "target": "secret" },
                { "name": "caption", "type": "text", "requiredWhen": { "and": [ { "field": "heading", "equals": "x" }, { "field": "pick", "isEmpty": true } ] } },
                { "name": "card", "type": "component", "component": "card" },
                { "name": "more", "type": "blocks", "blocks": ["leaf"] }
              ]
            },
            {
              "name": "leaf",
              "fields": [
                { "name": "pick", "type": "relation", "target": "secret" },
                { "name": "handle", "type": "slug", "source": "pick" },
                { "name": "note", "type": "text", "visibleWhen": { "field": "pick", "isEmpty": true } }
              ]
            }
          ]
        }
        """).Build();

    private static readonly EffectiveGrant SchemaRead = new("schema.read", PermissionConstraints.None, null);

    private static readonly EffectiveGrant Everything = new("*", PermissionConstraints.None, null);

    private static readonly EffectiveGrant PageWithoutTitle =
        new("content.page.read", new PermissionConstraints { Fields = ["summary", "slug", "intro", "extra", "rank", "related", "body"] }, null);

    [Fact]
    public async Task ARelationInsideAPartToAnUnreadableTypeIsLeftOut()
    {
        var described = await DescribeAsync(new EffectiveGrant("content.page.read", PermissionConstraints.None, null));

        described.Types.Select(type => type.Name).ShouldBe(["page"]);
        described.Components.Single(part => part.Name == "card").Fields.Select(field => field.Name).ShouldBe(["label"]);
        described.Blocks.ShouldHaveSingleItem().Fields.Select(field => field.Name).ShouldBe(["heading"]);
    }

    [Fact]
    public async Task APartNothingUsesStaysAndOneOnlyALeftOutFieldUsedGoes()
    {
        var described = await DescribeAsync(new EffectiveGrant("content.page.read", new PermissionConstraints { Fields = ["title"] }, null));

        described.Types.ShouldHaveSingleItem().Fields.Select(field => field.Name).ShouldBe(["title"]);
        described.Components.Select(part => part.Name).ShouldBe(["spare"]);
        described.Blocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task AReaderOfEverythingGetsTheInstanceSnapshot()
    {
        var introspection = Introspection(Everything);

        (await introspection.SnapshotAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(SchemaSnapshot.Of(Model));
    }

    [Fact]
    public async Task AConditionOrASlugSourceThatReadsAFieldOutsideTheAllowListIsLeftOut()
    {
        var described = await DescribeAsync(Conditional, PageWithoutTitle, new EffectiveGrant("content.secret.read", PermissionConstraints.None, null));

        var page = Type(described, "page");
        page.Fields.Select(field => field.Name).ShouldBe(["summary", "slug", "intro", "extra", "rank", "related", "body"]);
        Field(page, "slug").Settings.ShouldBeEmpty();
        Field(page, "intro").VisibleWhen.ShouldBeNull();
        Field(page, "intro").RequiredWhen!.Field.ShouldBe("summary");

        // Every field of the parts stays, and so does every condition and slug source in them.
        Field(Part(described.Blocks, "teaser"), "caption").RequiredWhen!.Operands!.Count.ShouldBe(2);
        Field(Part(described.Blocks, "leaf"), "handle").Settings[FieldSettings.Source].ShouldBe("pick");
        Field(Part(described.Components, "card"), "hint").VisibleWhen!.Field.ShouldBe("link");
    }

    [Fact]
    public async Task AFieldReferenceOfAPluginTypeThatReadsAFieldOutsideTheAllowListIsLeftOut()
    {
        var model = FieldTypes.ReferenceFieldType.Builder()
            .AddCollection("memo", type => type
                .Field("title", "text")
                .Field("body", "text")
                .Field("summary", FieldTypes.ReferenceFieldType.TypeName, field => field.Setting(FieldTypes.ReferenceFieldType.From, "title"))
                .Field("teaser", FieldTypes.ReferenceFieldType.TypeName, field => field.Setting(FieldTypes.ReferenceFieldType.From, "body")))
            .Build();

        var described = await DescribeAsync(model, new EffectiveGrant("content.memo.read", new PermissionConstraints { Fields = ["body", "summary", "teaser"] }, null));

        var memo = Type(described, "memo");
        Field(memo, "summary").Settings.ShouldBeEmpty();
        Field(memo, "teaser").Settings[FieldTypes.ReferenceFieldType.From].ShouldBe("body");
    }

    // Without one of its clauses an "or" would hold less often and an "and" more often than the condition declared.
    [Fact]
    public async Task AConditionGoesWholeWhenOnlyOneOfItsClausesReadsAFieldThatWent()
    {
        var full = await DescribeAsync(Conditional, Everything);
        var described = await DescribeAsync(Conditional, PageWithoutTitle);

        Field(Type(full, "page"), "extra").VisibleWhen!.Operands!.Count.ShouldBe(2);
        Field(Type(described, "page"), "extra").VisibleWhen.ShouldBeNull();
        Field(Type(described, "page"), "extra").RequiredWhen!.Operands!.Count.ShouldBe(2);

        Field(Part(full.Blocks, "teaser"), "caption").RequiredWhen.ShouldNotBeNull();
        Field(Part(described.Blocks, "teaser"), "caption").RequiredWhen.ShouldBeNull();
    }

    [Fact]
    public async Task AConditionOrASlugSourceInANestedPartThatReadsALeftOutRelationIsLeftOut()
    {
        var described = await DescribeAsync(Conditional, new EffectiveGrant("content.page.read", PermissionConstraints.None, null));

        var page = Type(described, "page");
        Field(page, "slug").Settings[FieldSettings.Source].ShouldBe("title");
        Field(page, "intro").VisibleWhen!.Field.ShouldBe("title");

        var teaser = Part(described.Blocks, "teaser");
        teaser.Fields.Select(field => field.Name).ShouldBe(["heading", "caption", "card", "more"]);
        Field(teaser, "caption").RequiredWhen.ShouldBeNull();

        var card = Part(described.Components, "card");
        card.Fields.Select(field => field.Name).ShouldBe(["label", "hint"]);
        Field(card, "hint").VisibleWhen.ShouldBeNull();
        Field(card, "hint").RequiredWhen!.Field.ShouldBe("label");

        var leaf = Part(described.Blocks, "leaf");
        leaf.Fields.Select(field => field.Name).ShouldBe(["handle", "note"]);
        Field(leaf, "handle").Settings.ShouldBeEmpty();
        Field(leaf, "note").VisibleWhen.ShouldBeNull();
    }

    [Fact]
    public async Task NoNameOfAFieldThatWentShowsAnywhere()
    {
        string[] gone = ["\"title\"", "\"pick\"", "\"link\"", "\"secret\""];
        var cancellationToken = TestContext.Current.CancellationToken;
        var full = JsonSerializer.Serialize(await Introspection(Conditional, Everything).DescribeAsync(cancellationToken));
        var introspection = Introspection(Conditional, SchemaRead, PageWithoutTitle);

        string[] texts =
        [
            JsonSerializer.Serialize(await introspection.DescribeAsync(cancellationToken)),
            JsonSerializer.Serialize(await introspection.DescribeTypeAsync("page", cancellationToken)),
            (await introspection.SnapshotAsync(cancellationToken)).Json,
        ];

        gone.ShouldAllBe(name => full.Contains(name, StringComparison.Ordinal));
        texts.ShouldAllBe(text => gone.All(name => !text.Contains(name, StringComparison.Ordinal)));
        Should.NotThrow(() => new SchemaBuilder().AddSchemaJson(texts[2]).Build());
    }

    [Fact]
    public async Task AFieldThatLosesAConditionOrASlugSourceKeepsEverythingElse()
    {
        var full = Type(await DescribeAsync(Conditional, Everything), "page");
        var described = Type(await DescribeAsync(Conditional, PageWithoutTitle), "page");

        foreach (var name in new[] { "extra", "rank", "related" })
        {
            JsonSerializer.Serialize(Field(described, name)).ShouldBe(JsonSerializer.Serialize(Field(full, name) with { VisibleWhen = null }));
        }

        JsonSerializer.Serialize(Field(described, "slug")).ShouldBe(JsonSerializer.Serialize(Field(full, "slug") with { Settings = new Dictionary<string, object>() }));
    }

    [Fact]
    public async Task ATitleFieldOutsideTheAllowListIsLeftOutAndTheDisplayNameStays()
    {
        var model = new SchemaBuilder().AddCollection("page", type => type
            .DisplayName("Web page")
            .TitleField("title")
            .Field("title", "text")
            .Field("summary", "text")).Build();

        var full = Type(await DescribeAsync(model, Everything), "page");
        var withTitle = Type(await DescribeAsync(model, new EffectiveGrant("content.page.read", new PermissionConstraints { Fields = ["title"] }, null)), "page");
        var withoutTitle = Type(await DescribeAsync(model, new EffectiveGrant("content.page.read", new PermissionConstraints { Fields = ["summary"] }, null)), "page");

        (full.DisplayName, full.TitleField).ShouldBe(("Web page", "title"));
        (withTitle.DisplayName, withTitle.TitleField).ShouldBe(("Web page", "title"));
        (withoutTitle.DisplayName, withoutTitle.TitleField).ShouldBe(("Web page", (string?)null));
    }

    [Fact]
    public async Task ATypeIsPreviewableWhenTheSiteHasATemplateForIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var introspection = Introspection(Conditional, new Dictionary<string, string> { ["page"] = "https://www.example.com/{locale}/{slug}?preview={token}" }, TokenScopes.Management, SchemaRead, Everything);

        var described = await introspection.DescribeAsync(cancellationToken);

        Type(described, "page").Previewable.ShouldBeTrue();
        Type(described, "secret").Previewable.ShouldBeFalse();
        described.Components.Concat(described.Blocks).ShouldAllBe(part => !part.Previewable);
        (await introspection.DescribeTypeAsync("page", cancellationToken))!.Previewable.ShouldBeTrue();
        (await introspection.DescribeTypeAsync("secret", cancellationToken))!.Previewable.ShouldBeFalse();
        JsonSerializer.Serialize(Type(described, "page") with { Previewable = false }).ShouldBe(JsonSerializer.Serialize(Type(await DescribeAsync(Conditional, Everything), "page")));
    }

    // The token's URL is filled from the caller's own read, so a template that reads a field it may not read gives none.
    [Fact]
    public async Task ATemplateThatReadsAFieldOutsideTheAllowListIsNone()
    {
        var templates = new Dictionary<string, string> { ["page"] = "https://www.example.com/{title}?preview={token}" };

        var all = await Introspection(Conditional, templates, TokenScopes.Management, SchemaRead, Everything).DescribeAsync(TestContext.Current.CancellationToken);
        var limited = await Introspection(Conditional, templates, TokenScopes.Management, SchemaRead, PageWithoutTitle).DescribeAsync(TestContext.Current.CancellationToken);

        Type(all, "page").Previewable.ShouldBeTrue();
        Type(limited, "page").Previewable.ShouldBeFalse();
    }

    // Minting is a route of the Management API, which needs the management scope; an MCP client reads the schema without it.
    [Fact]
    public async Task ACallerWhoMayNotMintSeesNoTypePreviewable()
    {
        var templates = new Dictionary<string, string> { ["page"] = "https://www.example.com/{slug}?preview={token}" };

        var mcp = await Introspection(Conditional, templates, TokenScopes.Mcp, SchemaRead, Everything).DescribeAsync(TestContext.Current.CancellationToken);
        var unfit = await Introspection(Conditional, new Dictionary<string, string> { ["page"] = "https://www.example.com/{dropped}?preview={token}" }, TokenScopes.Management, SchemaRead, Everything)
            .DescribeAsync(TestContext.Current.CancellationToken);

        mcp.Types.ShouldAllBe(type => !type.Previewable);
        unfit.Types.ShouldAllBe(type => !type.Previewable);
    }

    private static SchemaTypeDescription Type(SchemaDescription described, string name) => Part(described.Types, name);

    private static SchemaTypeDescription Part(IReadOnlyList<SchemaTypeDescription> types, string name) => types.Single(type => type.Name == name);

    private static SchemaFieldDescription Field(SchemaTypeDescription type, string name) => type.Fields.Single(field => field.Name == name);

    private static Task<SchemaDescription> DescribeAsync(params EffectiveGrant[] grants) => DescribeAsync(Model, grants);

    private static Task<SchemaDescription> DescribeAsync(SchemaModel model, params EffectiveGrant[] grants) =>
        Introspection(model, [SchemaRead, .. grants]).DescribeAsync(TestContext.Current.CancellationToken);

    private static SchemaIntrospection Introspection(params EffectiveGrant[] grants) => Introspection(Model, grants);

    private static SchemaIntrospection Introspection(SchemaModel model, params EffectiveGrant[] grants) =>
        Introspection(model, [], TokenScopes.Management, grants);

    private static SchemaIntrospection Introspection(SchemaModel model, Dictionary<string, string> templates, string scope, params EffectiveGrant[] grants)
    {
        var principal = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "admin", new Dictionary<string, string> { [TokenScopes.Claim] = scope });
        var origin = scope == TokenScopes.Mcp ? Origin.Mcp : Origin.Admin;
        var operation = new OperationContext(principal, origin, new SiteId(Guid.CreateVersion7()), "en", null, "test", DateTimeOffset.UtcNow);
        var permissions = new EffectivePermissions(operation, grants, new RowRuleRegistry(model, new ContentQueryOptions(), []));
        var evaluator = new FixedEvaluator(permissions);
        var accessor = new FixedAccessor { Current = operation };
        return new SchemaIntrospection(model, evaluator, new ManagementAuthority(evaluator, accessor), accessor, new FixedPreviews(new PreviewSettings { Templates = templates }));
    }

    private sealed class FixedPreviews(PreviewSettings settings) : ISettings<PreviewSettings>
    {
        public ValueTask<PreviewSettings> GetAsync(SiteId site, CancellationToken cancellationToken = default) => ValueTask.FromResult(settings);

        public ValueTask<ResolvedSettings<PreviewSettings>> ResolveAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PreviewSettings> GetDefaultAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetAsync(SiteId site, PreviewSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetDefaultAsync(PreviewSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ResetAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ResetDefaultAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedAccessor : IOperationContextAccessor
    {
        public OperationContext? Current { get; set; }
    }

    private sealed class FixedEvaluator(EffectivePermissions permissions) : IAuthorizationEvaluator
    {
        public ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(permissions);

        public ValueTask<bool> CanAccessEntryAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WritableFields> WritableFieldsAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid? entry, AccessTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
