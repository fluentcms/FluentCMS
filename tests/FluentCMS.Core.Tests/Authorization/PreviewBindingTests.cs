using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// The entry and locale a preview token binds a principal to, as the engine applies them.
public sealed class PreviewBindingTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());
    private static readonly SchemaType Page = new SchemaBuilder().AddCollection("page", type => type.Field("title", "text")).Build().ContentTypes.Single();
    private static readonly EffectiveGrant ReadPages = new("content.page.read", PermissionConstraints.None, roleSite: null);

    [Fact]
    public void TheEntryIsAddedToTheRowRule()
    {
        var entry = Guid.CreateVersion7();

        var rule = Permissions(entry.ToString("D"), locale: null).RowRuleOf(Page, ContentOperation.Read, new AccessTarget(Site, "en"));

        rule.ShouldBe(new FieldFilter("id", FilterOperator.Equal, entry));
    }

    [Fact]
    public void ADamagedEntryClaimReachesNoRow()
    {
        var rule = Permissions("not-an-id", locale: null).RowRuleOf(Page, ContentOperation.Read, new AccessTarget(Site, "en"));

        rule.ShouldBeSameAs(RowRule.NoRows);
    }

    // The values a locale falls back to are read in other locales, so the engine does not hold a token to its locale.
    [Fact]
    public void ABoundLocaleLeavesTheFallbackLocalesReadable()
    {
        var permissions = Permissions(Guid.CreateVersion7().ToString("D"), "fr");

        permissions.Allows(Page, ContentOperation.Read, new AccessTarget(Site, "fr")).ShouldBeTrue();
        permissions.Allows(Page, ContentOperation.Read, new AccessTarget(Site, "en")).ShouldBeTrue();
        permissions.Allows(Page, ContentOperation.Read, new AccessTarget(new SiteId(Guid.CreateVersion7()), "fr")).ShouldBeFalse();
    }

    [Fact]
    public void AnUnboundPrincipalKeepsItsRule()
    {
        var principal = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "admin", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Preview });

        PrincipalBinding.Of(principal).ShouldBe(new PrincipalBinding(null, null));
        Permissions(principal).RowRuleOf(Page, ContentOperation.Read, new AccessTarget(Site, "en")).ShouldBeNull();
    }

    // A host without the identity knows a principal by its claims alone, so a preview token's targets stay held to the
    // type of its entry there; with the identity, the minter's own binding is read instead.
    [Fact]
    public async Task WithoutTheIdentityTheTargetsStayHeldToTheTypeOfTheEntry()
    {
        var roles = new ClaimPrincipalRoles();
        var preview = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "editor", new Dictionary<string, string> { [CmsClaimTypes.Types] = "page" });

        (await roles.TypesOfAsync(preview, TestContext.Current.CancellationToken)).ShouldBe(new HashSet<string> { "page" });
        (await roles.TypesOfAsync(new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "admin"), TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    private static EffectivePermissions Permissions(string entry, string? locale)
    {
        Dictionary<string, string> claims = new()
        {
            [TokenScopes.Claim] = TokenScopes.Preview,
            [CmsClaimTypes.Sites] = Site.Value.ToString("D"),
            [CmsClaimTypes.Types] = "page",
            [CmsClaimTypes.Entry] = entry,
        };
        if (locale is not null)
        {
            claims[CmsClaimTypes.Locale] = locale;
        }

        return Permissions(new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "editor", claims));
    }

    private static EffectivePermissions Permissions(Principal principal) =>
        new(new OperationContext(principal, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow), [ReadPages], new RowRuleRegistry(new SchemaBuilder().Build(), new ContentQueryOptions(), []));
}
