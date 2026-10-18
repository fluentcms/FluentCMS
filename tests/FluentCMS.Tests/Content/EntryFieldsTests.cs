using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using static FluentCMS.Tests.Content.TestEntries;

namespace FluentCMS.Tests.Content;

public sealed class EntryFieldsTests
{
    [Fact]
    public void A_valid_document_is_stored_in_the_definitions_order_without_nulls()
    {
        var fields = Json($$"""
            {"product": "{{ProductId}}", "tags": ["new", "sale"], "body": null, "rating": 4, "headline": "A fine widget"}
            """);

        var stored = EntryFields.Validate(fields, Page, Registry).Value;

        Assert.Equal(
            $$"""{"headline":"A fine widget","rating":4,"tags":["new","sale"],"product":"{{ProductId}}"}""",
            stored.GetRawText());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"A fine widget\"")]
    [InlineData("null")]
    public void A_document_that_is_not_an_object_is_malformed(string json)
    {
        var error = Assert.Single(EntryFields.Validate(Json(json), Page, Registry).Errors);

        Assert.Equal(ContentErrorCodes.FieldsMalformed, error.Code);
    }

    [Fact]
    public void Every_problem_comes_back_at_once_and_names_its_field()
    {
        var fields = Json("""{"headlines": "Typo", "rating": 9, "tags": ["ok", 5], "product": "widget"}""");

        var errors = EntryFields.Validate(fields, Page, Registry).Errors;

        Assert.Equal(
            [
                (ContentErrorCodes.FieldUnknown, "There is no field 'headlines'; the fields are headline, body, rating, tags, product."),
                (ContentErrorCodes.FieldRequired, "Field 'headline' is required."),
                (SchemaErrorCodes.ValueInvalid, "Field 'rating': The value must be at most 5."),
                (SchemaErrorCodes.ValueInvalid, "Field 'tags': Item 2: The value must be a string."),
                (SchemaErrorCodes.ValueInvalid,
                    $"Field 'product': The value must be the id of an entry of the content type 'product', a UUID such as {FieldValueDescription.ExampleId}."),
            ],
            errors.Select(error => (error.Code, error.Message)));
    }

    [Fact]
    public void An_explicit_null_reads_as_an_absent_value()
    {
        var error = Assert.Single(EntryFields.Validate(Json("""{"headline": null}"""), Page, Registry).Errors);

        Assert.Equal(ContentErrorCodes.FieldRequired, error.Code);
        Assert.Equal("{}", EntryFields.Validate(Json("""{"body": null}"""), new ContentTypeDefinition([Field("body", "richText")]), Registry).Value.GetRawText());
    }

    [Fact]
    public void Field_names_are_matched_exactly()
    {
        var errors = EntryFields.Validate(Json("""{"Headline": "Case matters"}"""), Page, Registry).Errors;

        Assert.Equal([ContentErrorCodes.FieldUnknown, ContentErrorCodes.FieldRequired], errors.Select(error => error.Code));
    }

    [Fact]
    public void A_field_given_twice_is_malformed()
    {
        using var document = JsonDocument.Parse("""{"headline": "One", "headline": "Two"}""");

        var error = Assert.Single(EntryFields.Validate(document.RootElement, Page, Registry).Errors);

        Assert.Equal(ContentErrorCodes.FieldsMalformed, error.Code);
        Assert.Equal("The field 'headline' is given more than once.", error.Message);
    }

    [Fact]
    public void A_type_without_fields_says_so()
    {
        var error = Assert.Single(EntryFields.Validate(Json("""{"title": "x"}"""), new ContentTypeDefinition([]), Registry).Errors);

        Assert.Equal("There is no field 'title'; this content type has no fields.", error.Message);
    }

    [Fact]
    public void A_value_of_a_field_type_that_is_not_installed_is_refused()
    {
        var definition = new ContentTypeDefinition([Field("rating", "acme.rating")]);

        var error = Assert.Single(EntryFields.Validate(Json("""{"rating": 5}"""), definition, Registry).Errors);

        Assert.Equal(SchemaErrorCodes.FieldTypeUnknown, error.Code);
        Assert.StartsWith("Field 'rating': ", error.Message);
        Assert.True(EntryFields.Validate(Json("{}"), definition, Registry).IsSuccess);
    }

    [Fact]
    public void An_entity_stores_each_value_as_a_fact_with_its_metadata()
    {
        var fields = Json("""
            {
              "name": {"value": "Widget", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {
                "value": 19.99,
                "source": {"kind": "url", "reference": "https://example.com/prices"},
                "owner": "0199a000-0000-7000-8000-000000000002",
                "validFrom": "2026-01-01T00:00:00Z",
                "validUntil": "2026-12-31T23:59:59+01:00",
                "lastVerified": "2026-10-01T08:00:00Z"
              }
            }
            """);

        var stored = EntryFields.Validate(fields, Product, Registry);

        Assert.True(stored.IsSuccess, string.Join("; ", stored.Errors.Select(error => error.Message)));
        Assert.True(JsonElement.DeepEquals(fields, stored.Value));
    }

    [Fact]
    public void A_fact_is_checked_for_its_value_and_every_metadata_property()
    {
        var fields = Json("""
            {
              "name": "Widget",
              "price": {
                "value": -1,
                "source": {"kind": "rumour", "reference": "a friend"},
                "owner": "amir",
                "validFrom": "2026-01-01",
                "lastVerified": "yesterday",
                "confidence": "high"
              },
              "launched": {"source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
            }
            """);

        var errors = EntryFields.Validate(fields, Product, Registry).Errors;

        Assert.All(errors, error => Assert.Equal(SchemaErrorCodes.ValueInvalid, error.Code));
        Assert.Equal(
            [
                "Field 'name': A fact must be an object with value, source, owner, validFrom, validUntil, lastVerified.",
                "Field 'price': A fact has no property 'confidence'; it has value, source, owner, validFrom, validUntil, lastVerified.",
                "Field 'price': The value must be at least 0.",
                "Field 'price': The fact's source must be null or an object with 'kind', one of url, file, person, system, and 'reference', "
                    + "the URL, file, person or system it names.",
                "Field 'price': The fact's owner must be the id of a user, a UUID, or null.",
                "Field 'price': The fact's 'validFrom' must be a point in time in ISO 8601 with an offset, such as 2026-01-31T09:30:00Z, or null.",
                "Field 'price': The fact's 'validUntil' is missing; give null when it has none.",
                "Field 'price': The fact's 'lastVerified' must be a point in time in ISO 8601 with an offset, such as 2026-01-31T09:30:00Z, or null.",
                "Field 'launched': The fact has no value; leave the field out instead.",
            ],
            errors.Select(error => error.Message));
    }

    [Theory]
    [InlineData("""{"kind": "url"}""")]
    [InlineData("""{"kind": "url", "reference": " "}""")]
    [InlineData("""{"kind": "url", "reference": "https://example.com", "note": "x"}""")]
    [InlineData("\"https://example.com\"")]
    public void A_source_names_its_kind_and_reference_and_nothing_else(string source)
    {
        var fields = Json($$"""
            {
              "name": {"value": "Widget", "source": {{source}}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {"value": 1, "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
            }
            """);

        var error = Assert.Single(EntryFields.Validate(fields, Product, Registry).Errors);

        Assert.StartsWith("Field 'name': The fact's source must be null or an object", error.Message);
    }
}
