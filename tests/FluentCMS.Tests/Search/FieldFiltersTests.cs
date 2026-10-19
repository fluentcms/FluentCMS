using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Search;

namespace FluentCMS.Tests.Search;

public sealed class FieldFiltersTests
{
    private static readonly ContentTypeDefinition Product = new(
    [
        new FieldDefinition("name", "text"),
        new FieldDefinition("price", "number"),
        new FieldDefinition("launch", "dateTime"),
        new FieldDefinition("active", "boolean"),
        new FieldDefinition("category", "reference", settings: Json("""{"target": "category"}""")),
        new FieldDefinition("tags", "list", settings: Json("""{"itemType": "text"}""")),
    ]);

    [Fact]
    public void Each_field_type_takes_its_own_operators_and_a_value_of_its_kind()
    {
        var refused = FieldFilters.Create(
            [
                Filter("colour", FieldOperator.EqualTo, "\"red\""),
                Filter("tags", FieldOperator.EqualTo, "\"sale\""),
                Filter("price", FieldOperator.Contains, "\"19\""),
                Filter("price", FieldOperator.LessThan, "\"20\""),
                Filter("launch", FieldOperator.AtLeast, "\"next spring\""),
                Filter("active", FieldOperator.EqualTo, "\"yes\""),
                Filter("category", FieldOperator.EqualTo, "\"not-an-id\""),
                Filter("name", FieldOperator.Contains, "\"\""),
            ],
            "product",
            Product);

        Assert.Equal(
            [
                SearchErrorCodes.FieldNotFound,
                SearchErrorCodes.FieldNotFilterable,
                SearchErrorCodes.OperatorNotSupported,
                SearchErrorCodes.FilterValueInvalid,
                SearchErrorCodes.FilterValueInvalid,
                SearchErrorCodes.FilterValueInvalid,
                SearchErrorCodes.FilterValueInvalid,
                SearchErrorCodes.FilterValueInvalid,
            ],
            refused.Errors.Select(error => error.Code));
        Assert.Contains("use equal_to, less_than, at_most, greater_than, at_least", refused.Errors[2].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name", FieldOperator.EqualTo, "\"Widget Pro\"", true)]
    [InlineData("name", FieldOperator.EqualTo, "\"widget pro\"", false)]
    [InlineData("name", FieldOperator.Contains, "\"WIDG\"", true)]
    [InlineData("price", FieldOperator.LessThan, "20", true)]
    [InlineData("price", FieldOperator.AtMost, "19.99", true)]
    [InlineData("price", FieldOperator.GreaterThan, "19.99", false)]
    [InlineData("price", FieldOperator.AtLeast, "19.99", true)]
    [InlineData("launch", FieldOperator.LessThan, "\"2026-11-01T00:00:00Z\"", true)]
    [InlineData("launch", FieldOperator.GreaterThan, "\"2026-11-01T00:00:00+02:00\"", false)]
    [InlineData("active", FieldOperator.EqualTo, "true", true)]
    [InlineData("active", FieldOperator.EqualTo, "false", false)]
    [InlineData("category", FieldOperator.EqualTo, "\"0192F3A1-7C2E-7A4B-9D3E-1F2A3B4C5D6E\"", true)]
    public void A_filter_holds_by_its_operator(string field, FieldOperator op, string value, bool holds)
    {
        var filters = FieldFilters.Create([Filter(field, op, value)], "product", Product).Value;

        Assert.Equal(holds, filters.Matches(Json("""
            {
                "name": "Widget Pro",
                "price": 19.99,
                "launch": "2026-10-19T09:00:00Z",
                "active": true,
                "category": "0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e",
                "tags": ["sale"]
            }
            """)));
    }

    [Fact]
    public void A_missing_or_null_field_holds_no_filter_and_every_filter_must_hold()
    {
        var filters = FieldFilters.Create(
            [Filter("price", FieldOperator.LessThan, "20"), Filter("name", FieldOperator.Contains, "\"pro\"")],
            "product",
            Product).Value;

        Assert.True(filters.Matches(Json("""{"name": "Widget Pro", "price": 10}""")));
        Assert.False(filters.Matches(Json("""{"name": "Widget", "price": 10}""")));
        Assert.False(filters.Matches(Json("""{"name": "Widget Pro", "price": null}""")));
        Assert.False(filters.Matches(Json("""{"name": "Widget Pro"}""")));
    }

    [Fact]
    public void An_entitys_field_is_compared_by_its_facts_value()
    {
        var entity = new ContentTypeDefinition([new FieldDefinition("price", "number")], isEntity: true);
        var filters = FieldFilters.Create([Filter("price", FieldOperator.LessThan, "20")], "product", entity).Value;

        Assert.True(filters.Matches(Json("""{"price": {"value": 19.99, "source": null, "owner": null}}""")));
        Assert.False(filters.Matches(Json("""{"price": {"value": 25, "source": null, "owner": null}}""")));
        Assert.True(FieldFilters.None.Matches(Json("{}")));
    }

    private static FieldFilter Filter(string field, FieldOperator op, string value)
    {
        return new FieldFilter(field, op, Json(value));
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
