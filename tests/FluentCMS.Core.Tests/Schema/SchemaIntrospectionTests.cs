using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

// What the Blog sample cannot show: relations inside components and blocks, and parts that nothing uses.
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
        var introspection = Introspection(new EffectiveGrant("*", PermissionConstraints.None, null));

        (await introspection.SnapshotAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(SchemaSnapshot.Of(Model));
    }

    private static Task<SchemaDescription> DescribeAsync(params EffectiveGrant[] grants) =>
        Introspection([new EffectiveGrant("schema.read", PermissionConstraints.None, null), .. grants]).DescribeAsync(TestContext.Current.CancellationToken);

    private static SchemaIntrospection Introspection(params EffectiveGrant[] grants)
    {
        var principal = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "admin", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });
        var operation = new OperationContext(principal, Origin.Admin, new SiteId(Guid.CreateVersion7()), "en", null, "test", DateTimeOffset.UtcNow);
        var permissions = new EffectivePermissions(operation, grants, new RowRuleRegistry(Model, new ContentQueryOptions(), []));
        var evaluator = new FixedEvaluator(permissions);
        var accessor = new FixedAccessor { Current = operation };
        return new SchemaIntrospection(Model, evaluator, new ManagementAuthority(evaluator, accessor), accessor);
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
