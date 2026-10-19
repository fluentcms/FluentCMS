using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Routing;

namespace FluentCMS.Tests.Routing;

public sealed class RoutingRuleDefinitionTests
{
    private const string PriceChanges = """
        {"description": "Price changes need a person.",
         "when": {"itemKinds": ["entity"], "contentTypes": ["product"], "fields": ["price"], "changeKinds": ["update"],
                  "initiators": ["agent"], "agents": ["0199f2a0-0000-7000-8000-000000000001"], "checks": "warn", "riskScore": {"min": 10, "max": 90}},
         "then": {"disposition": "review", "approvals": 2}}
        """;

    [Fact]
    public void A_definition_reads_every_condition_and_writes_back_the_same_document()
    {
        var definition = RoutingRuleDefinition.Parse(PriceChanges).Value;

        Assert.Equal("Price changes need a person.", definition.Description);
        Assert.Equal([ItemKind.Entity], definition.When.ItemKinds);
        Assert.Equal(["product"], definition.When.ContentTypes);
        Assert.Equal(["price"], definition.When.Fields);
        Assert.Equal([OperationKind.Update], definition.When.ChangeKinds);
        Assert.Equal([InitiatorCondition.Agent], definition.When.Initiators);
        Assert.Equal([Guid.Parse("0199f2a0-0000-7000-8000-000000000001")], definition.When.Agents);
        Assert.Equal(RoutingCheckOutcome.Warn, definition.When.Checks);
        Assert.Equal((10, 90), (definition.When.MinRiskScore, definition.When.MaxRiskScore));
        Assert.Equal(RoutingDisposition.Review(2), definition.Then);
        Assert.True(definition.Validate().IsSuccess);

        var again = RoutingRuleDefinition.Parse(definition.ToJson()).Value;
        Assert.Equal(definition.ToJson(), again.ToJson());
    }

    [Theory]
    [InlineData("""{"description": "Routine.", "then": {"disposition": "auto_publish"}}""", DispositionKind.AutoPublish, 0)]
    [InlineData("""{"description": "Legal.", "when": null, "then": {"disposition": "block"}}""", DispositionKind.Block, 0)]
    [InlineData("""{"description": "Default.", "then": {"disposition": "review"}}""", DispositionKind.Review, 1)]
    public void Conditions_may_be_left_out_and_a_review_needs_one_approval_unless_told_otherwise(string json, DispositionKind kind, int approvals)
    {
        var definition = RoutingRuleDefinition.Parse(json).Value;

        Assert.Same(RoutingConditions.Any, definition.When);
        Assert.Equal((kind, approvals), (definition.Then.Kind, definition.Then.Approvals));
        Assert.True(definition.Validate().IsSuccess);
    }

    [Fact]
    public void Every_malformed_part_is_reported_at_once()
    {
        var result = RoutingRuleDefinition.Parse("""
            {"description": 7, "when": {"itemKinds": ["page"], "colour": ["red"], "checks": "great", "riskScore": {"low": 1}},
             "then": {"disposition": "maybe"}, "extra": true}
            """);

        var messages = result.Errors.Select(error => error.Message).ToList();
        Assert.All(result.Errors, error => Assert.Equal(RoutingErrorCodes.DefinitionMalformed, error.Code));
        Assert.Contains(messages, message => message.Contains("$.description"));
        Assert.Contains(messages, message => message.Contains("'page'"));
        Assert.Contains(messages, message => message.Contains("$.when.colour"));
        Assert.Contains(messages, message => message.Contains("$.when.checks"));
        Assert.Contains(messages, message => message.Contains("$.when.riskScore"));
        Assert.Contains(messages, message => message.Contains("$.then.disposition"));
        Assert.Contains(messages, message => message.Contains("$.extra"));
    }

    [Theory]
    [InlineData("""{"description": "x", "then": {"disposition": "review", "approvals": 0}}""")]
    [InlineData("""{"description": "x", "then": {"disposition": "review", "approvals": 11}}""")]
    [InlineData("""{"description": "x", "then": {"disposition": "block", "approvals": 1}}""")]
    public void A_disposition_takes_approvals_only_for_a_review_and_only_from_one_to_ten(string json)
    {
        Assert.Equal(RoutingErrorCodes.DispositionInvalid, Assert.Single(RoutingRuleDefinition.Parse(json).Errors).Code);
    }

    [Fact]
    public void Validation_reports_every_bad_condition_value()
    {
        var definition = new RoutingRuleDefinition(
            " ",
            new RoutingConditions
            {
                ContentTypes = ["Product Page", "product", "product"],
                Fields = [],
                ItemKinds = [(ItemKind)42],
                Agents = [Guid.Empty],
                MinRiskScore = 80,
                MaxRiskScore = 20,
            },
            RoutingDisposition.Block);

        var errors = definition.Validate().Errors;

        Assert.Contains(errors, error => error.Code == RoutingErrorCodes.DescriptionInvalid);
        Assert.Contains(errors, error => error.Message.Contains("'Product Page'"));
        Assert.Contains(errors, error => error.Message.Contains("'product' more than once"));
        Assert.Contains(errors, error => error.Message.Contains("'fields' must not be empty"));
        Assert.Contains(errors, error => error.Message.Contains("'itemKinds' holds '42'"));
        Assert.Contains(errors, error => error.Message.Contains("an agent credential id"));
        Assert.Contains(errors, error => error.Message.Contains("min 80 is above its max 20"));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, 101)]
    public void Risk_score_bounds_are_from_zero_to_one_hundred(int? min, int? max)
    {
        var definition = new RoutingRuleDefinition("x", new RoutingConditions { MinRiskScore = min, MaxRiskScore = max }, RoutingDisposition.Block);

        Assert.Contains(definition.Validate().Errors, error => error.Code == RoutingErrorCodes.ConditionInvalid && error.Message.Contains("from 0 to 100"));
    }
}
