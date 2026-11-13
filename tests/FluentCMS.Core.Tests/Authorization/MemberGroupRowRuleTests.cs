using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// Principal.IsInGroup answers in a row rule only: the principal a rule is given carries the groups the evaluator read for
// a member, and the principal of the operation, which a hook, a job or an endpoint reads, never does.
public sealed class MemberGroupRowRuleTests
{
    private const string Rule = "in-group";
    private static readonly SiteId Site = new(Guid.CreateVersion7());
    private static readonly AccessTarget InSite = new(Site, "en");
    private static readonly Dictionary<string, string> GoldClaim = new() { [Principal.GroupsClaim] = "gold" };
    private static readonly SchemaModel Schema = new SchemaBuilder().AddCollection("perk", type => type.Field("tier", "text")).Build();
    private static readonly SchemaType Perk = Schema.ContentTypes.Single();

    [Theory]
    [InlineData("gold", "in")]
    [InlineData("early-access", "in")]
    [InlineData("Gold", "out")]
    [InlineData("early", "out")]
    [InlineData("gold early-access", "out")]
    [InlineData("", "out")]
    public void ARowRuleReadsTheGroupsOfAMember(string asked, string tier) =>
        TierOf(Member(), ["early-access", "gold"], asked).ShouldBe(tier);

    [Fact]
    public void AMemberInNoGroupIsInNone() =>
        TierOf(Member(), [], "gold").ShouldBe("out");

    [Fact]
    public void AGroupsClaimTheOperationsPrincipalHadIsNotRead() =>
        TierOf(Member(GoldClaim), [], "gold").ShouldBe("out");

    [Fact]
    public void OnlyAMemberIsInGroups()
    {
        TierOf(new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "Amir", GoldClaim), ["gold"], "gold").ShouldBe("out");
        TierOf(new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "Key"), null, "gold").ShouldBe("out");
        TierOf(Principal.Anonymous, null, "gold").ShouldBe("out");
    }

    [Fact]
    public void ThePrincipalOfTheOperationDoesNotCarryTheGroups()
    {
        var context = Context(Member());
        var permissions = Permissions(context, ["gold"], "gold");

        permissions.RowRuleOf(Perk, ContentOperation.Read, InSite).ShouldBe(new FieldFilter("tier", FilterOperator.Equal, "in"));
        Should.Throw<InvalidOperationException>(() => context.Principal.IsInGroup("gold")).Message.ShouldContain("outside a row rule");
    }

    private static string TierOf(Principal principal, IReadOnlyList<string>? groups, string asked) =>
        Permissions(Context(principal), groups, asked).RuleFilter(Rule, Perk)
            .ShouldBeOfType<FieldFilter>().Value.ShouldBeOfType<string>();

    private static EffectivePermissions Permissions(OperationContext context, IReadOnlyList<string>? groups, string asked)
    {
        var rule = RowRuleTranslator.Translate<TieredPerk>(Rule, "perk", ctx => perk => perk.Tier == (ctx.Principal.IsInGroup(asked) ? "in" : "out"));
        var rules = new RowRuleRegistry(Schema, new ContentQueryOptions(), [rule]);
        var grant = new EffectiveGrant("content.perk.read", new PermissionConstraints { RowRule = Rule }, roleSite: null);
        return new EffectivePermissions(context, [grant], rules, groups: groups);
    }

    private static Principal Member(Dictionary<string, string>? claims = null) =>
        new(PrincipalKind.Member, Guid.CreateVersion7(), "Sam", claims);

    private static OperationContext Context(Principal principal) =>
        new(principal, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow);

    public sealed class TieredPerk : ContentItem
    {
        public string? Tier { get; set; }
    }
}
