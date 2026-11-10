using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// A token of an MCP client that registered itself is held to the role of its client: every answer is what both the
// principal's grants and that role's give.
public sealed class ClientRoleLimitTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());
    private static readonly SchemaType Page = new SchemaBuilder()
        .AddCollection("page", type => type.Field("title", "text").Field("body", "text").Field("notes", "text"))
        .Build().ContentTypes.Single();

    private static readonly AccessTarget InSite = new(Site, "en");
    private static readonly EffectiveGrant Everything = new("*", PermissionConstraints.None, roleSite: null);

    [Fact]
    public void APermissionNeedsBothThePrincipalAndTheClientRole()
    {
        var reader = new EffectiveGrant("content.page.read", PermissionConstraints.None, null);

        var limited = Permissions([Everything], [reader]);

        limited.Allows(Page, ContentOperation.Read, InSite).ShouldBeTrue();
        limited.Allows(Page, ContentOperation.Update, InSite).ShouldBeFalse();
        limited.Allows("users.manage", new AccessTarget(null, null)).ShouldBeFalse();
        limited.CanReadDraft(Page, InSite).ShouldBeTrue();

        // The client's role gives nothing the principal lacks.
        var widened = Permissions([reader], [Everything]);
        widened.Allows(Page, ContentOperation.Update, InSite).ShouldBeFalse();
        widened.Allows("users.manage", new AccessTarget(null, null)).ShouldBeFalse();
        widened.Allows(Page, ContentOperation.Read, InSite).ShouldBeTrue();
    }

    [Fact]
    public void AClientRoleWithNoGrantLeavesNothing()
    {
        var permissions = Permissions([Everything], []);

        permissions.Allows(Page, ContentOperation.Read, InSite).ShouldBeFalse();
        permissions.Allows("mcp.manage", new AccessTarget(null, null)).ShouldBeFalse();
        permissions.GrantsOf(Page, ContentOperation.Read, InSite).ShouldBeEmpty();
        permissions.RowRuleOf(Page, ContentOperation.Read, InSite).ShouldBeSameAs(RowRule.NoRows);
        permissions.ReadableFieldsOf(Page, InSite).ShouldNotBeNull().ShouldBeEmpty();
        permissions.ResourcesOf("media.read", InSite).ShouldNotBeNull().ShouldBeEmpty();
        permissions.Covers(new GrantReach("content.page.read", null, null, null, null)).ShouldBeFalse();
        Permissions([Everything], [Everything]).Covers(new GrantReach("content.page.read", null, null, null, null)).ShouldBeTrue();
    }

    [Fact]
    public void SitesFieldsLocalesAndResourcesAreWhatBothAllow()
    {
        var other = new SiteId(Guid.CreateVersion7());
        var own = new EffectiveGrant("content.page.*", new PermissionConstraints { Fields = ["title", "body"] }, null);
        var client = new EffectiveGrant("content.page.read", new PermissionConstraints { Fields = ["body", "notes"], Sites = [Site], Locales = ["en"] }, null);

        var permissions = Permissions([own, new EffectiveGrant("media.read", new PermissionConstraints { Resources = ["a", "b"] }, null)], [client, new EffectiveGrant("media.read", new PermissionConstraints { Resources = ["b", "c"] }, null)]);

        permissions.ReadableFieldsOf(Page, InSite).ShouldNotBeNull().ShouldBe(["body"]);
        permissions.CanReadField(Page, "body", InSite).ShouldBeTrue();
        permissions.CanReadField(Page, "title", InSite).ShouldBeFalse();
        permissions.Allows(Page, ContentOperation.Read, new AccessTarget(other, "en")).ShouldBeFalse();
        permissions.Allows(Page, ContentOperation.Read, new AccessTarget(Site, "fr")).ShouldBeFalse();
        permissions.ReadLocalesOf(Page, new AccessTarget(Site, null)).ShouldNotBeNull().ShouldBe(["en"]);
        permissions.ReadsEveryRow(Page, InSite).ShouldBeFalse();
        permissions.ResourcesOf("media.read", InSite).ShouldNotBeNull().ShouldBe(["b"]);

        // With no limit of its own, the client's role leaves the principal's answer as it is.
        var unlimited = Permissions([own], [Everything]);
        unlimited.ReadableFieldsOf(Page, InSite).ShouldNotBeNull().ShouldBe(["title", "body"], ignoreOrder: true);
        unlimited.ReadLocalesOf(Page, new AccessTarget(Site, null)).ShouldBeNull();
        unlimited.ReadsEveryRow(Page, InSite).ShouldBeTrue();
    }

    [Fact]
    public void WritableFieldsAreWhatBothAllow()
    {
        new WritableFields(true, null).And(new WritableFields(true, new HashSet<string> { "title" })).Fields.ShouldBe(["title"]);
        new WritableFields(true, new HashSet<string> { "title", "body" }).And(new WritableFields(true, new HashSet<string> { "body", "notes" })).Fields.ShouldBe(["body"]);
        new WritableFields(true, null).And(WritableFields.All).ShouldBe(WritableFields.All);
        WritableFields.All.And(WritableFields.None).ShouldBe(WritableFields.None);
        WritableFields.None.And(WritableFields.All).ShouldBe(WritableFields.None);
    }

    [Fact]
    public void APrincipalWithoutAClientRoleIsNotLimited()
    {
        var permissions = Permissions([Everything], client: null);

        permissions.Limit.ShouldBeNull();
        permissions.Allows(Page, ContentOperation.Update, InSite).ShouldBeTrue();
        permissions.RowRuleOf(Page, ContentOperation.Read, InSite).ShouldBeNull();
        permissions.ReadableFieldsOf(Page, InSite).ShouldBeNull();
    }

    private static EffectivePermissions Permissions(IReadOnlyList<EffectiveGrant> own, IReadOnlyList<EffectiveGrant>? client)
    {
        var principal = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "editor", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });
        var context = new OperationContext(principal, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow);
        var rules = new RowRuleRegistry(new SchemaBuilder().Build(), new ContentQueryOptions(), []);
        return new EffectivePermissions(context, own, rules, client is null ? null : new EffectivePermissions(context, client, rules));
    }
}
