#pragma warning disable CA1861
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Media;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Validation;

public class ConditionTests
{
    private static readonly Condition Video = Condition.Field("category").Equals(ArticleCategory.Video);
    private static readonly Condition NoTitle = Condition.Field("title").IsEmpty();

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value);

    public static TheoryData<string, Condition> Conditions => new()
    {
        { "equal", Video },
        { "equal number", Condition.Field("count").Equals(5) },
        { "equal bool", Condition.Field("flag").Equals(true) },
        { "equal null", Condition.Field("title").Equals(null) },
        { "in", Condition.Field("category").In(ArticleCategory.News, "video", 3) },
        { "isEmpty", NoTitle },
        { "and", Video.And(NoTitle) },
        { "or", Video.Or(NoTitle) },
        { "not", Video.Not() },
        { "nested", Video.And(NoTitle.Not()).Or(Condition.Field("count").In(1, 2.5m).Not()).And(Condition.Field("flag").Equals(false)) },
    };

    private static readonly Dictionary<string, object?>[] Samples =
    [
        Values(),
        Values(("category", ArticleCategory.Video), ("title", "T"), ("count", 5), ("flag", true)),
        Values(("category", "news"), ("title", ""), ("count", 2.5m), ("flag", false)),
        Values(("category", ArticleCategory.Opinion), ("title", null), ("count", 1L), ("flag", false)),
    ];

    [Theory]
    [MemberData(nameof(Conditions))]
    public void ConditionsRoundTripThroughJson(string name, Condition condition)
    {
        var json = ConditionJson.ToJson(condition);

        var read = ConditionJson.Parse(json);

        ConditionJson.ToJson(read).ShouldBe(json, name);
        read.ToString().ShouldBe(condition.ToString(), name);
        read.Operator.ShouldBe(condition.Operator, name);
    }

    [Theory]
    [MemberData(nameof(Conditions))]
    public void ConditionsEvaluateTheSameFromCSharpAndFromJson(string name, Condition condition)
    {
        var fromJson = ConditionJson.Parse(ConditionJson.ToJson(condition));

        foreach (var values in Samples)
        {
            ConditionEvaluator.Evaluate(fromJson, values).ShouldBe(ConditionEvaluator.Evaluate(condition, values), name);
        }
    }

    [Fact]
    public void JsonHasTheShapeOfTheSchemaFile()
    {
        ConditionJson.ToJson(Video).ShouldBe("""{"field":"category","equals":"video"}""");
        ConditionJson.ToJson(Condition.Field("a").In(1, "b")).ShouldBe("""{"field":"a","in":[1,"b"]}""");
        ConditionJson.ToJson(NoTitle).ShouldBe("""{"field":"title","isEmpty":true}""");
        ConditionJson.ToJson(Video.And(NoTitle.Not())).ShouldBe(
            """{"and":[{"field":"category","equals":"video"},{"not":{"field":"title","isEmpty":true}}]}""");
    }

    [Fact]
    public void AnInvalidConditionIsRejected()
    {
        Should.Throw<System.Text.Json.JsonException>(() => ConditionJson.Parse("""{"field":"a","equals":1,"in":[1]}"""))
            .Message.ShouldContain("exactly one");
        Should.Throw<System.Text.Json.JsonException>(() => ConditionJson.Parse("[]"));
    }

    [Fact]
    public void AnEnumValueMatchesItsCamelCaseName()
    {
        ConditionEvaluator.Evaluate(Video, Values(("category", ArticleCategory.Video))).ShouldBeTrue();
        ConditionEvaluator.Evaluate(Video, Values(("category", "video"))).ShouldBeTrue();
        ConditionEvaluator.Evaluate(Video, Values(("category", ArticleCategory.News))).ShouldBeFalse();
    }

    [Fact]
    public void StringsAreComparedOrdinally()
    {
        ConditionEvaluator.Evaluate(Condition.Field("a").Equals("x"), Values(("a", "X"))).ShouldBeFalse();
    }

    [Fact]
    public void NumbersAreComparedByValueAcrossTypes()
    {
        var five = Condition.Field("n").Equals(5);

        foreach (object value in new object[] { 5, 5L, 5m, 5.0m, (short)5, 5.0 })
        {
            ConditionEvaluator.Evaluate(five, Values(("n", value))).ShouldBeTrue(value.GetType().Name);
        }

        ConditionEvaluator.Evaluate(five, Values(("n", 6))).ShouldBeFalse();
        ConditionEvaluator.Evaluate(Condition.Field("n").Equals(2.5m), Values(("n", 2.5))).ShouldBeTrue();
        ConditionEvaluator.Evaluate(five, Values(("n", "5"))).ShouldBeFalse();
    }

    [Fact]
    public void AnUnknownFieldIsNull()
    {
        var values = Values();

        ConditionEvaluator.Evaluate(Condition.Field("missing").Equals(null), values).ShouldBeTrue();
        ConditionEvaluator.Evaluate(Condition.Field("missing").IsEmpty(), values).ShouldBeTrue();
        ConditionEvaluator.Evaluate(Condition.Field("missing").Equals("x"), values).ShouldBeFalse();
        ConditionEvaluator.Evaluate(Condition.Field("missing").In("x", null), values).ShouldBeTrue();
    }

    [Fact]
    public void AMultiSelectEqualsAValueItContains()
    {
        var values = Values(("labels", new[] { "a", "b" }));

        ConditionEvaluator.Evaluate(Condition.Field("labels").Equals("b"), values).ShouldBeTrue();
        ConditionEvaluator.Evaluate(Condition.Field("labels").Equals("c"), values).ShouldBeFalse();
        ConditionEvaluator.Evaluate(Condition.Field("labels").In("c", "a"), values).ShouldBeTrue();
        ConditionEvaluator.Evaluate(Condition.Field("labels").Equals(null), Values(("labels", Array.Empty<string>()))).ShouldBeTrue();
    }

    [Fact]
    public void AndOrAndNotCombineTheirOperands()
    {
        var values = Values(("a", 1));
        var holds = Condition.Field("a").Equals(1);
        var fails = Condition.Field("a").Equals(2);

        ConditionEvaluator.Evaluate(holds.And(holds), values).ShouldBeTrue();
        ConditionEvaluator.Evaluate(holds.And(fails), values).ShouldBeFalse();
        ConditionEvaluator.Evaluate(fails.Or(holds), values).ShouldBeTrue();
        ConditionEvaluator.Evaluate(fails.Or(fails), values).ShouldBeFalse();
        ConditionEvaluator.Evaluate(fails.Not(), values).ShouldBeTrue();
    }

    public static TheoryData<string, object?, bool> IsEmptyMatrix => new()
    {
        { "null", null, true },
        { "empty string", "", true },
        { "whitespace", "  \t", true },
        { "empty array", Array.Empty<string>(), true },
        { "empty list", new List<int>(), true },
        { "empty block list", new BlockList(), true },
        { "empty ref list", new RefList<Tag>(), true },
        { "empty media list", new MediaRefList(), true },
        { "default media", default(MediaRef), true },
        { "default ref", default(Ref<Tag>), true },
        { "empty guid", Guid.Empty, true },
        { "text", "x", false },
        { "zero", 0, false },
        { "false", false, false },
        { "list", new[] { "a" }, false },
        { "block list", new BlockList([new HeroBlock()]), false },
        { "ref list", new RefList<Tag>([new Ref<Tag>(Guid.NewGuid())]), false },
        { "ref", new Ref<Tag>(Guid.NewGuid()), false },
        { "media", new MediaRef(Guid.NewGuid()), false },
        { "enum", ArticleCategory.News, false },
    };

    [Theory]
    [MemberData(nameof(IsEmptyMatrix))]
    public void IsEmptyFollowsTheEmptinessRules(string name, object? value, bool expected)
    {
        ConditionEvaluator.Evaluate(Condition.Field("f").IsEmpty(), Values(("f", value))).ShouldBe(expected, name);
    }
}
