using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class DescriptorTests
{
    private static readonly FilterOperator[] Text = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn, FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.IsNull, FilterOperator.IsNotNull];

    private static readonly FilterOperator[] Ordered = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn, FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual, FilterOperator.Between, FilterOperator.IsNull, FilterOperator.IsNotNull];

    private static readonly FilterOperator[] NullChecks = [FilterOperator.IsNull, FilterOperator.IsNotNull];

    public static TheoryData<string, FieldStorageKind, FieldStorageKind, bool, bool, FilterOperator[]> Descriptors => new()
    {
        { "text", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Text },
        { "longText", FieldStorageKind.Column, FieldStorageKind.Column, false, false, [FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.IsNull, FilterOperator.IsNotNull] },
        { "richText", FieldStorageKind.Json, FieldStorageKind.Json, false, false, NullChecks },
        { "integer", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Ordered },
        { "decimal", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Ordered },
        { "boolean", FieldStorageKind.Column, FieldStorageKind.Column, true, true, [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.IsNull, FilterOperator.IsNotNull] },
        { "date", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Ordered },
        { "dateTime", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Ordered },
        { "email", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Text },
        { "url", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Text },
        { "slug", FieldStorageKind.Column, FieldStorageKind.Column, true, true, Text },
        { "select", FieldStorageKind.Column, FieldStorageKind.Json, true, true, [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn, FilterOperator.Contains, FilterOperator.IsNull, FilterOperator.IsNotNull] },
        { "json", FieldStorageKind.Json, FieldStorageKind.Json, false, true, [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Contains, FilterOperator.IsNull, FilterOperator.IsNotNull] },
        { "relation", FieldStorageKind.Reference, FieldStorageKind.Reference, false, false, [FilterOperator.Equal, FilterOperator.In, FilterOperator.IsNull, FilterOperator.IsNotNull] },
        { "component", FieldStorageKind.Column, FieldStorageKind.Json, false, false, [] },
        { "blocks", FieldStorageKind.Json, FieldStorageKind.Json, false, true, [FilterOperator.HasBlock, FilterOperator.IsNull, FilterOperator.IsNotNull] },
    };

    public static TheoryData<string, string> OpenApiFragments => new()
    {
        { "text", """{"type":"string"}""" },
        { "longText", """{"type":"string"}""" },
        { "richText", """{"type":"object","properties":{"type":{"const":"doc"},"content":{"type":"array","items":{"type":"object"}}},"required":["type"]}""" },
        { "integer", """{"type":"integer","format":"int64"}""" },
        { "decimal", """{"type":"number"}""" },
        { "boolean", """{"type":"boolean"}""" },
        { "date", """{"type":"string","format":"date"}""" },
        { "dateTime", """{"type":"string","format":"date-time"}""" },
        { "email", """{"type":"string","format":"email"}""" },
        { "url", """{"type":"string","format":"uri"}""" },
        { "slug", """{"type":"string","pattern":"^[a-z0-9]+(?:-[a-z0-9]+)*$"}""" },
        { "select", """{"type":"string","enum":["news","opinion"]}""" },
        { "json", "{}" },
        { "relation", """{"type":"string","format":"uuid"}""" },
        { "component", """{"$ref":"#/components/schemas/component-seo"}""" },
        { "blocks", """{"type":"array","items":{"oneOf":[{"$ref":"#/components/schemas/block-hero"}],"discriminator":{"propertyName":"type","mapping":{"hero":"#/components/schemas/block-hero"}}}}""" },
    };

    [Theory]
    [MemberData(nameof(Descriptors))]
    public void DescriptorsFollowTheStorageAndQueryModel(string name, FieldStorageKind column, FieldStorageKind repeated, bool sortable, bool indexable, FilterOperator[] operators)
    {
        var type = TestFields.Type(name);

        type.Name.ShouldBe(name);
        type.StorageKind(TestFields.Field(name)).ShouldBe(column);
        type.StorageKind(TestFields.Field(name, field => field.Multiple())).ShouldBe(repeated);

        var single = TestFields.Field(name);
        var many = TestFields.Field(name, field => field.Multiple());
        type.Sortable(single).ShouldBe(sortable);
        type.Sortable(many).ShouldBe(sortable && repeated != FieldStorageKind.Json);
        type.Indexable(single).ShouldBe(indexable);
        type.Indexable(many).ShouldBe(indexable && (repeated != FieldStorageKind.Json || column == FieldStorageKind.Json));
        type.FilterOperators.OrderBy(op => op).ShouldBe(operators.OrderBy(op => op));
        type.ClrTypes.ShouldNotBeEmpty();
        type.AcceptsAnySetting.ShouldBeFalse();
    }

    [Fact]
    public void AFieldStoredAsJsonIsNeverSortable()
    {
        foreach (var type in FieldTypeRegistry.Core.All)
        {
            foreach (var multiple in new[] { false, true })
            {
                var field = TestFields.Field(type.Name, f => f.Multiple(multiple));
                if (type.StorageKind(field) == FieldStorageKind.Json)
                {
                    type.Sortable(field).ShouldBeFalse($"{type.Name}, multiple {multiple}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(OpenApiFragments))]
    public void OpenApiFragmentHasTheExpectedShape(string name, string expected)
    {
        var actual = TestFields.Type(name).OpenApiSchema(TestFields.Field(name));

        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).ShouldBeTrue(actual.ToJsonString());
    }

    [Fact]
    public void OpenApiFragmentIsANewObjectEachTime()
    {
        var field = TestFields.Field("text");
        var type = TestFields.Type("text");

        type.OpenApiSchema(field).ShouldNotBeSameAs(type.OpenApiSchema(field));
    }

    [Fact]
    public void TextFragmentCarriesTheLimitsOfTheField()
    {
        var field = TestFields.Field("text", f => f.Length(2, 50).Pattern("^a"));

        JsonNode.DeepEquals(TestFields.Type("text").OpenApiSchema(field), JsonNode.Parse("""{"type":"string","minLength":2,"maxLength":50,"pattern":"^a"}""")).ShouldBeTrue();
    }

    [Fact]
    public void SlugFragmentKeepsTheSlugPatternAndTheLength()
    {
        var field = TestFields.Field("slug", f => f.Length(null, 80).Pattern("^a"));

        JsonNode.DeepEquals(TestFields.Type("slug").OpenApiSchema(field), JsonNode.Parse("""{"type":"string","pattern":"^[a-z0-9]+(?:-[a-z0-9]+)*$","maxLength":80}""")).ShouldBeTrue();
    }

    [Fact]
    public void NumberFragmentsCarryTheRange()
    {
        var integer = TestFields.Field("integer", f => f.Range(1, 10));
        var number = TestFields.Field("decimal", f => f.Range(0.5m, null).Setting("scale", 2));

        JsonNode.DeepEquals(TestFields.Type("integer").OpenApiSchema(integer), JsonNode.Parse("""{"type":"integer","format":"int64","minimum":1,"maximum":10}""")).ShouldBeTrue();
        JsonNode.DeepEquals(TestFields.Type("decimal").OpenApiSchema(number), JsonNode.Parse("""{"type":"number","minimum":0.5}""")).ShouldBeTrue();
    }

    [Fact]
    public void MultipleFieldsDescribeAnArray()
    {
        var select = TestFields.Field("select", f => f.Multiple());
        var relation = TestFields.Field("relation", f => f.Multiple());
        var component = TestFields.Field("component", f => f.Multiple());

        JsonNode.DeepEquals(TestFields.Type("select").OpenApiSchema(select), JsonNode.Parse("""{"type":"array","items":{"type":"string","enum":["news","opinion"]},"uniqueItems":true}""")).ShouldBeTrue();
        JsonNode.DeepEquals(TestFields.Type("relation").OpenApiSchema(relation), JsonNode.Parse("""{"type":"array","items":{"type":"string","format":"uuid"}}""")).ShouldBeTrue();
        JsonNode.DeepEquals(TestFields.Type("component").OpenApiSchema(component), JsonNode.Parse("""{"type":"array","items":{"$ref":"#/components/schemas/component-seo"}}""")).ShouldBeTrue();
    }

    [Fact]
    public void CoreTypesDeclareTheirSettings()
    {
        TestFields.Type("select").Settings.ShouldBe([new FieldSettingDescriptor { Key = "options", Kind = FieldSettingKind.StringList, Required = true }]);
        TestFields.Type("component").Settings.ShouldBe([new FieldSettingDescriptor { Key = "component", Kind = FieldSettingKind.String, Required = true }]);
        TestFields.Type("blocks").Settings.ShouldBe([new FieldSettingDescriptor { Key = "blocks", Kind = FieldSettingKind.StringList, Required = true }]);
        TestFields.Type("slug").Settings.ShouldBe([new FieldSettingDescriptor { Key = "source", Kind = FieldSettingKind.String }]);
        TestFields.Type("decimal").Settings.ShouldBe([new FieldSettingDescriptor { Key = "precision", Kind = FieldSettingKind.Integer }, new FieldSettingDescriptor { Key = "scale", Kind = FieldSettingKind.Integer }]);

        foreach (var name in new[] { "text", "longText", "richText", "integer", "boolean", "date", "dateTime", "email", "url", "json", "relation" })
        {
            TestFields.Type(name).Settings.ShouldBeEmpty(name);
        }
    }

    [Fact]
    public void ListsOfTheRegistryAndItsTypesCannotBeChanged()
    {
        var type = TestFields.Type("decimal");

        Should.Throw<NotSupportedException>(() => ((IList<Type>)type.ClrTypes).Add(typeof(int)));
        Should.Throw<NotSupportedException>(() => ((IList<FieldSettingDescriptor>)type.Settings).Clear());
        Should.Throw<NotSupportedException>(() => ((IList<IFieldType>)FieldTypeRegistry.Core.All).Clear());
        FieldTypeRegistry.Core.All.Count.ShouldBe(16);
    }

    [Fact]
    public void ClrTypesNameThePropertyTypesOfEachType()
    {
        TestFields.Type("text").ClrTypes.ShouldBe([typeof(string)]);
        TestFields.Type("richText").ClrTypes.ShouldBe([typeof(RichText)]);
        TestFields.Type("integer").ClrTypes.ShouldContain(typeof(int));
        TestFields.Type("integer").ClrTypes.ShouldContain(typeof(long?));
        TestFields.Type("date").ClrTypes.ShouldContain(typeof(DateOnly));
        TestFields.Type("dateTime").ClrTypes.ShouldContain(typeof(DateTimeOffset?));
        TestFields.Type("relation").ClrTypes.ShouldBe([typeof(Ref<>), typeof(RefList<>)]);
        TestFields.Type("blocks").ClrTypes.ShouldBe([typeof(BlockList)]);
    }
}
