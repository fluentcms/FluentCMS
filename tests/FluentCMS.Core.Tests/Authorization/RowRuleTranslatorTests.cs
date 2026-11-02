using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

public class RowRuleTranslatorTests
{
    private static readonly Guid UserId = Guid.Parse("0192e0a0-0000-7000-8000-0000000000a1");
    private static readonly string[] EuAndUs = ["eu", "us"];
    private static readonly string[] EuOnly = ["eu"];

    private static OperationContext Context(params (string Type, string Value)[] claims) => new(
        new Principal(PrincipalKind.AdminUser, UserId, "Amir", claims.Select(claim => KeyValuePair.Create(claim.Type, claim.Value))),
        Origin.Api,
        new SiteId(Guid.CreateVersion7()),
        "en",
        null,
        "correlation",
        DateTimeOffset.UtcNow);

    private static RowRule Translate<T>(Expression<Func<OperationContext, Expression<Func<T, bool>>>> rule)
        where T : ContentItem => RowRuleTranslator.Translate("test", "test", rule);

    private static ContentFilter Filter<T>(Expression<Func<OperationContext, Expression<Func<T, bool>>>> rule, OperationContext? context = null)
        where T : ContentItem => Translate(rule).For(context ?? Context());

    [Fact]
    public void TheSpecificationsRulesRegisterInTheirOwnSyntax()
    {
        var builder = new AuthorizationBuilder();

        builder.AddRowRule<Article>("own-entries", ctx => article => article.CreatedBy == ctx.Principal.Id);
        builder.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region"));

        builder.Rules.Select(rule => (rule.Name, rule.TypeName)).ShouldBe(
            [
                ("own-entries", null),
                ("own-entries", "article"),
                ("region", "regional"),
            ]);
    }

    [Fact]
    public void OwnEntriesComparesTheSystemField() =>
        Filter<Article>(ctx => article => article.CreatedBy == ctx.Principal.Id)
            .ShouldBe(new FieldFilter("createdBy", FilterOperator.Equal, UserId));

    [Fact]
    public void RegionComparesAColumnWithAClaim() =>
        Filter<Regional>(ctx => article => article.Region == ctx.Principal.Claim("region"), Context(("region", "eu")))
            .ShouldBe(new FieldFilter("region", FilterOperator.Equal, "eu"));

    [Fact]
    public void AValueIsReadForEveryOperationNotOnce()
    {
        var rule = Translate<Regional>(ctx => article => article.Region == ctx.Principal.Claim("region"));

        rule.For(Context(("region", "eu"))).ShouldBe(new FieldFilter("region", FilterOperator.Equal, "eu"));
        rule.For(Context(("region", "us"))).ShouldBe(new FieldFilter("region", FilterOperator.Equal, "us"));
    }

    [Fact]
    public void ALocalVariableIsAValue()
    {
        var region = "eu";

        Filter<Regional>(ctx => article => article.Region == region).ShouldBe(new FieldFilter("region", FilterOperator.Equal, "eu"));
    }

    [Fact]
    public void TheValueMayComeFirstAndTheOperatorTurnsAround()
    {
        Filter<Regional>(ctx => article => "eu" == article.Region).ShouldBe(new FieldFilter("region", FilterOperator.Equal, "eu"));
        Filter<Regional>(ctx => article => 5 < article.Views).ShouldBe(new FieldFilter("views", FilterOperator.GreaterThan, 5));
        Filter<Regional>(ctx => article => 5 >= article.Views).ShouldBe(new FieldFilter("views", FilterOperator.LessThanOrEqual, 5));
    }

    [Fact]
    public void EveryComparisonOperatorIsTranslated()
    {
        Filter<Regional>(ctx => article => article.Region != "eu").ShouldBe(new FieldFilter("region", FilterOperator.NotEqual, "eu"));
        Filter<Regional>(ctx => article => article.Views < 5).ShouldBe(new FieldFilter("views", FilterOperator.LessThan, 5));
        Filter<Regional>(ctx => article => article.Views <= 5).ShouldBe(new FieldFilter("views", FilterOperator.LessThanOrEqual, 5));
        Filter<Regional>(ctx => article => article.Views > 5).ShouldBe(new FieldFilter("views", FilterOperator.GreaterThan, 5));
        Filter<Regional>(ctx => article => article.Views >= 5).ShouldBe(new FieldFilter("views", FilterOperator.GreaterThanOrEqual, 5));
    }

    [Fact]
    public void AndOrAndNotBecomeNodes()
    {
        Filter<Regional>(ctx => article => article.Region == "eu" && article.Views > 1)
            .ShouldBe(new And(new FieldFilter("region", FilterOperator.Equal, "eu"), new FieldFilter("views", FilterOperator.GreaterThan, 1)));
        Filter<Regional>(ctx => article => article.Region == "eu" || article.CreatedBy == ctx.Principal.Id)
            .ShouldBe(new Or(new FieldFilter("region", FilterOperator.Equal, "eu"), new FieldFilter("createdBy", FilterOperator.Equal, UserId)));
        Filter<Regional>(ctx => article => !(article.Region == "eu"))
            .ShouldBe(new Not(new FieldFilter("region", FilterOperator.Equal, "eu")));
    }

    [Fact]
    public void ABooleanFieldStandsAlone()
    {
        Filter<Regional>(ctx => article => article.Featured).ShouldBe(new FieldFilter("featured", FilterOperator.Equal, true));
        Filter<Regional>(ctx => article => !article.Featured).ShouldBe(new FieldFilter("featured", FilterOperator.Equal, false));
    }

    [Fact]
    public void ALiteralNullIsAnOperator()
    {
        Filter<Regional>(ctx => article => article.Region == null).ShouldBe(new FieldFilter("region", FilterOperator.IsNull));
        Filter<Regional>(ctx => article => article.Region != null).ShouldBe(new FieldFilter("region", FilterOperator.IsNotNull));
    }

    [Fact]
    public void ANestedAndInheritedMemberIsAPath()
    {
        Filter<Article>(ctx => article => article.Seo.MetaTitle == "x").ShouldBe(new FieldFilter("seo.metaTitle", FilterOperator.Equal, "x"));
        Filter<Article>(ctx => article => article.Author.Id == ctx.Principal.Id).ShouldBe(new FieldFilter("author.id", FilterOperator.Equal, UserId));
    }

    [Fact]
    public void AnEnumComesBackAsItsMember()
    {
        Filter<Article>(ctx => article => article.Category == ArticleCategory.Opinion)
            .ShouldBe(new FieldFilter("category", FilterOperator.Equal, ArticleCategory.Opinion));
        Filter<Article>(ctx => article => article.Status == EntryStatus.Published)
            .ShouldBe(new FieldFilter("status", FilterOperator.Equal, EntryStatus.Published));
    }

    [Fact]
    public void ContainsAndStartsWithAreTranslated()
    {
        string[] regions = ["eu", "us"];
        List<string> list = ["eu"];

        Filter<Regional>(ctx => article => article.Region!.Contains("eu")).ShouldBe(new FieldFilter("region", FilterOperator.Contains, "eu"));
        Filter<Regional>(ctx => article => article.Region!.StartsWith("eu")).ShouldBe(new FieldFilter("region", FilterOperator.StartsWith, "eu"));
        Filter<Regional>(ctx => article => regions.Contains(article.Region!)).ShouldBe(new FieldFilter("region", FilterOperator.In, EuAndUs));
        Filter<Regional>(ctx => article => list.Contains(article.Region!)).ShouldBe(new FieldFilter("region", FilterOperator.In, EuOnly));
        Filter<Regional>(ctx => article => article.Labels.Contains("net")).ShouldBe(new FieldFilter("labels", FilterOperator.Contains, "net"));
    }

    [Fact]
    public void AMissingValueMatchesNoRow()
    {
        Filter<Regional>(ctx => article => article.Region == ctx.Principal.Claim("region")).ShouldBe(RowRule.NoRows);
    }

    [Fact]
    public void AMissingValueUnderANotOrAnOrClosesTheWholeRule()
    {
        Filter<Regional>(ctx => article => article.Region != ctx.Principal.Claim("region")).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => !(article.Region == ctx.Principal.Claim("region"))).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => article.Featured || article.Region == ctx.Principal.Claim("region")).ShouldBe(RowRule.NoRows);
    }

    [Fact]
    public void AnEmptyListOrTheEmptyIdMatchesNoRow()
    {
        string[] none = [];
        string?[] onlyNull = [null];

        Filter<Regional>(ctx => article => none.Contains(article.Region!)).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => onlyNull.Contains(article.Region)).ShouldBe(RowRule.NoRows);

        var anonymous = Context() with { Principal = Principal.Anonymous };
        Filter<Regional>(ctx => article => article.CreatedBy == ctx.Principal.Id, anonymous).ShouldBe(RowRule.NoRows);
    }

    [Fact]
    public void EmptyTextMatchesNoRow()
    {
        var empty = Context(("tenant", ""));

        Filter<Regional>(ctx => article => article.Region!.StartsWith(ctx.Principal.Claim("tenant")!), empty).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => article.Region!.Contains(ctx.Principal.Claim("tenant")!), empty).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => article.Region == ctx.Principal.Claim("tenant"), empty).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => article.Region != ctx.Principal.Claim("tenant"), empty).ShouldBe(RowRule.NoRows);
    }

    [Fact]
    public void AMissingValueInsideAListClosesTheRule()
    {
        var anonymous = Context() with { Principal = Principal.Anonymous };
        string[] withEmpty = ["eu", ""];
        string?[] withNull = ["eu", null];
        Guid[] others = [Guid.CreateVersion7()];

        Filter<Regional>(ctx => article => new List<Guid> { ctx.Principal.Id }.Contains(article.CreatedBy), anonymous).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => new List<Guid> { others[0], ctx.Principal.Id }.Contains(article.CreatedBy), anonymous).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => withEmpty.Contains(article.Region!)).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => withNull.Contains(article.Region)).ShouldBe(RowRule.NoRows);
        Filter<Regional>(ctx => article => !withEmpty.Contains(article.Region!)).ShouldBe(RowRule.NoRows);
    }

    public static TheoryData<Expression<Func<OperationContext, Expression<Func<Regional, bool>>>>> Unsupported => new()
    {
        ctx => article => true,
        ctx => article => article.Region == article.Note,
        ctx => article => ctx.Principal.Id == Guid.Empty,
        ctx => article => article.Region!.Trim() == "EU",
        ctx => article => ctx.Principal.Claim("regions")!.Contains(article.Region!),
        ctx => article => !ctx.Principal.Claim("regions")!.Contains(article.Region!),
        ctx => article => "eu,us".Contains(article.Region!),
        ctx => article => article.Views + 1 > 2,
        ctx => article => article.Region!.Contains("eu", StringComparison.Ordinal),
        ctx => article => article.Region!.StartsWith('e'),
        ctx => article => article.Region == null ? article.Featured : article.Views > 1,
        ctx => MadeElsewhere(),
    };

    private static Expression<Func<Regional, bool>> MadeElsewhere() => article => article.Featured;

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void AShapeThatCannotBeTranslatedIsRefusedWhenItIsRegistered(Expression<Func<OperationContext, Expression<Func<Regional, bool>>>> rule)
    {
        var refused = Should.Throw<FluentCmsConfigurationException>(() => new AuthorizationBuilder().AddRowRule("bad", rule));

        refused.Code.ShouldBe(AuthorizationErrorCodes.UnsupportedRowRule);
        refused.Message.ShouldContain("'bad'");
    }

    private static RowRuleRegistry Registry(AuthorizationBuilder builder) =>
        new(SampleSchema.FromClasses().Build(), new ContentQueryOptions(), builder.Rules);

    [Fact]
    public void ARuleForATypeWinsOverOneOnContentItem()
    {
        var registry = Registry(new AuthorizationBuilder().AddRowRule<Article>(BuiltInRoles.OwnEntriesRule, ctx => article => article.UpdatedBy == ctx.Principal.Id));

        registry.Find(BuiltInRoles.OwnEntriesRule, "article")!.Conditions.Single().Path.ShouldBe("updatedBy");
        registry.Find(BuiltInRoles.OwnEntriesRule, "author")!.Conditions.Single().Path.ShouldBe("createdBy");
        registry.Find("nothing", "article").ShouldBeNull();
    }

    [Fact]
    public void AFieldThatIsNotInTheSchemaStopsTheHost() =>
        Should.Throw<FluentCmsConfigurationException>(() => Registry(new AuthorizationBuilder().AddRowRule<NotQuiteArticle>("bad", ctx => article => article.Missing == "x")))
            .Code.ShouldBe(AuthorizationErrorCodes.InvalidRowRule);

    [Fact]
    public void AnOperatorTheFieldDoesNotAllowStopsTheHost() =>
        Should.Throw<FluentCmsConfigurationException>(() => Registry(new AuthorizationBuilder().AddRowRule<Article>("bad", ctx => article => article.Slug.Contains("xy") && article.CreatedBy > Guid.Empty)))
            .Code.ShouldBe(AuthorizationErrorCodes.InvalidRowRule);

    [Fact]
    public void AMemberOfAValueThatIsNotAFieldStopsTheHost() =>
        Should.Throw<FluentCmsConfigurationException>(() => Registry(new AuthorizationBuilder().AddRowRule<Article>("bad", ctx => article => article.Slug.Length > 2)))
            .Code.ShouldBe(AuthorizationErrorCodes.InvalidRowRule);

    [Fact]
    public void AGoingFurtherThanARelationHasTargetsStopsTheHost() =>
        Should.Throw<FluentCmsConfigurationException>(() => Registry(new AuthorizationBuilder().AddRowRule<Article>("bad", ctx => article => article.Author.IsEmpty)))
            .Code.ShouldBe(AuthorizationErrorCodes.InvalidRowRule);

    [Fact]
    public void AClassThatIsNotAContentTypeOfTheSchemaStopsTheHost() =>
        Should.Throw<FluentCmsConfigurationException>(() => Registry(new AuthorizationBuilder().AddRowRule<Regional>("bad", ctx => article => article.Region == "eu")))
            .Code.ShouldBe(AuthorizationErrorCodes.InvalidRowRule);

    [Fact]
    public void ARuleRegisteredTwiceForATypeStopsTheHost() =>
        Should.Throw<FluentCmsConfigurationException>(() => Registry(new AuthorizationBuilder()
                .AddRowRule<Article>("twice", ctx => article => article.Slug == "a")
                .AddRowRule<Article>("twice", ctx => article => article.Slug == "b")))
            .Code.ShouldBe(AuthorizationErrorCodes.DuplicateRowRule);

    public sealed class Regional : ContentItem
    {
        public string? Region { get; set; }

        public string? Note { get; set; }

        public int Views { get; set; }

        public bool Featured { get; set; }

        public List<string> Labels { get; set; } = [];
    }

    [FluentCMS.Schema.Collection("article")]
    public sealed class NotQuiteArticle : ContentItem
    {
        public string? Missing { get; set; }
    }
}
