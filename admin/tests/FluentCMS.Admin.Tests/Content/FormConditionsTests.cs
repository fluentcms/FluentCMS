using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

// visibleWhen and requiredWhen read as the server's ConditionEvaluator reads them, operator by operator.
public sealed class FormConditionsTests
{
    private static readonly JsonObject Values = new()
    {
        ["kind"] = "link",
        ["count"] = 3,
        ["featured"] = true,
        ["tags"] = new JsonArray("news", "tech"),
        ["none"] = new JsonArray(),
        ["blank"] = "  ",
        ["author"] = new JsonObject { ["id"] = "0193a1b2-0000-7000-8000-0000000000aa" },
        ["noAuthor"] = new JsonObject { ["id"] = Guid.Empty.ToString("D") },
        ["emptyBody"] = new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph" }) },
        ["body"] = new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Hi" }) }),
        },
    };

    [Theory]
    [InlineData("kind", "\"link\"", true)]
    [InlineData("kind", "\"Link\"", false)]
    [InlineData("count", "3", true)]
    [InlineData("count", "3.0", true)]
    [InlineData("count", "4", false)]
    [InlineData("featured", "true", true)]
    [InlineData("featured", "false", false)]
    [InlineData("tags", "\"tech\"", true)]
    [InlineData("tags", "\"sport\"", false)]
    [InlineData("none", "null", true)]
    [InlineData("missing", "null", true)]
    [InlineData("author", "\"0193a1b2-0000-7000-8000-0000000000aa\"", true)]
    [InlineData("count", "\"3\"", false)]
    public void EqualComparesAsTheServerDoes(string field, string value, bool expected) =>
        FormConditions.Evaluate(Condition("equal", field, JsonNode.Parse(value)), Values).ShouldBe(expected);

    [Fact]
    public void InHoldsWhenOneValueIsEqual()
    {
        FormConditions.Evaluate(Condition("in", "kind", "page", "link"), Values).ShouldBe(true);
        FormConditions.Evaluate(Condition("in", "kind", "page", "post"), Values).ShouldBe(false);
        FormConditions.Evaluate(Condition("in", "tags", "sport", "news"), Values).ShouldBe(true);
        FormConditions.Evaluate(new ConditionDescription { Operator = "in", Field = "kind" }, Values).ShouldBe(false);
    }

    [Theory]
    [InlineData("missing", true)]
    [InlineData("blank", true)]
    [InlineData("none", true)]
    [InlineData("noAuthor", true)]
    [InlineData("emptyBody", true)]
    [InlineData("kind", false)]
    [InlineData("count", false)]
    [InlineData("featured", false)]
    [InlineData("tags", false)]
    [InlineData("author", false)]
    [InlineData("body", false)]
    public void IsEmptyHoldsForNoValue(string field, bool expected) =>
        FormConditions.Evaluate(new ConditionDescription { Operator = "isEmpty", Field = field }, Values).ShouldBe(expected);

    [Fact]
    public void AndOrAndNotCombineTheirOperands()
    {
        var link = Condition("equal", "kind", "link");
        var page = Condition("equal", "kind", "page");

        FormConditions.Evaluate(Combined("and", link, Condition("equal", "featured", true)), Values).ShouldBe(true);
        FormConditions.Evaluate(Combined("and", link, page), Values).ShouldBe(false);
        FormConditions.Evaluate(Combined("or", page, link), Values).ShouldBe(true);
        FormConditions.Evaluate(Combined("or", page, page), Values).ShouldBe(false);
        FormConditions.Evaluate(Combined("not", page), Values).ShouldBe(true);
        FormConditions.Evaluate(Combined("not", link), Values).ShouldBe(false);
    }

    [Fact]
    public void AnOperatorThisBuildDoesNotKnowDecidesNothing()
    {
        var unknown = new ConditionDescription { Operator = "matches", Field = "kind" };
        var field = new SchemaFieldDescription { Name = "url", Type = "text", Validation = new(), VisibleWhen = unknown, RequiredWhen = unknown };

        FormConditions.Evaluate(unknown, Values).ShouldBeNull();
        FormConditions.Evaluate(Combined("and", Condition("equal", "kind", "link"), unknown), Values).ShouldBeNull();
        FormConditions.Evaluate(Combined("and", Condition("equal", "kind", "page"), unknown), Values).ShouldBe(false);
        FormConditions.Evaluate(Combined("or", Condition("equal", "kind", "link"), unknown), Values).ShouldBe(true);
        FormConditions.Shows(field, Values).ShouldBeTrue();
        FormConditions.Requires(field, Values).ShouldBeFalse();
    }

    [Fact]
    public void AFieldIsRequiredByItsValidationOrItsCondition()
    {
        var always = new SchemaFieldDescription { Name = "title", Type = "text", Validation = new() { Required = true } };
        var forLinks = new SchemaFieldDescription { Name = "url", Type = "text", Validation = new(), RequiredWhen = Condition("equal", "kind", "link") };
        var forPages = forLinks with { RequiredWhen = Condition("equal", "kind", "page") };

        FormConditions.Requires(always, Values).ShouldBeTrue();
        FormConditions.Requires(forLinks, Values).ShouldBeTrue();
        FormConditions.Requires(forPages, Values).ShouldBeFalse();
    }

    private static ConditionDescription Condition(string op, string field, params JsonNode?[] values) =>
        new() { Operator = op, Field = field, Values = new JsonArray([.. values.Select(value => value?.DeepClone())]) };

    private static ConditionDescription Combined(string op, params ConditionDescription[] operands) => new() { Operator = op, Operands = operands };
}
