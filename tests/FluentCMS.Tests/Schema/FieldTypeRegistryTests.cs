using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

public sealed class FieldTypeRegistryTests
{
    [Fact]
    public void Core_registers_the_SCH_02_field_types_as_built_ins()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

        Assert.Equal(
            ["boolean", "dateTime", "enum", "json", "list", "media", "number", "reference", "richText", "text"],
            provider.GetRequiredService<FieldTypeRegistry>().Names);

        var descriptors = provider.GetRequiredService<IExtensionCatalog>().For<IFieldType>();
        Assert.Equal(10, descriptors.Count);
        Assert.All(descriptors, descriptor => Assert.True(descriptor.IsBuiltIn));
    }

    [Fact]
    public void Two_field_types_with_the_same_name_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new FieldTypeRegistry([new StubFieldType<object>("sample.rating"), new RatingFieldType()]));

        Assert.Contains("'sample.rating'", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Text")]
    [InlineData("rich text")]
    [InlineData("acme.")]
    [InlineData("acme..rating")]
    public void A_field_type_with_an_invalid_name_is_rejected(string name)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new FieldTypeRegistry([new StubFieldType<object>(name)]));

        Assert.Contains("not a valid field type name", exception.Message);
    }

    [Fact]
    public void A_value_of_a_type_that_is_not_installed_is_refused_rather_than_stored_unchecked()
    {
        var error = Assert.Single(CoreRegistry().ValidateValue("money", FieldDefinition.EmptySettings, JsonSerializer.Deserialize<JsonElement>("5")));

        Assert.Equal(SchemaErrorCodes.FieldTypeUnknown, error.Code);
        Assert.Equal("The field type 'money' is not installed, so a value of it cannot be checked.", error.Message);
    }

    [Fact]
    public void An_unknown_type_is_reported_with_the_known_types()
    {
        var error = Assert.Single(CoreRegistry().ValidateSettings("money", FieldDefinition.EmptySettings));

        Assert.Equal(SchemaErrorCodes.FieldTypeUnknown, error.Code);
        Assert.Equal(
            "There is no field type 'money'; the known types are boolean, dateTime, enum, json, list, media, number, reference, richText, text.",
            error.Message);
    }

    [Fact]
    public void Names_are_matched_exactly()
    {
        Assert.NotNull(CoreRegistry().Find("text"));
        Assert.Null(CoreRegistry().Find("Text"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"maxLength\"")]
    [InlineData("null")]
    public void Settings_that_are_not_an_object_are_rejected_before_the_type_sees_them(string json)
    {
        var error = Assert.Single(CoreRegistry().ValidateSettings("text", JsonSerializer.Deserialize<JsonElement>(json)));

        Assert.Equal(SchemaErrorCodes.SettingInvalid, error.Code);
    }

    [Fact]
    public void An_unknown_setting_names_the_settings_the_type_has()
    {
        var errors = CoreRegistry().ValidateSettings("text", JsonSerializer.Deserialize<JsonElement>("""{"maxLenght": 10}"""));

        var error = Assert.Single(errors);
        Assert.Equal(SchemaErrorCodes.SettingUnknown, error.Code);
        Assert.Equal("The 'text' field type has no setting 'maxLenght'; its settings are maxLength.", error.Message);
    }

    [Fact]
    public void A_setting_on_a_type_without_settings_says_so()
    {
        var error = Assert.Single(CoreRegistry().ValidateSettings("boolean", JsonSerializer.Deserialize<JsonElement>("""{"default": true}""")));

        Assert.Equal("The 'boolean' field type takes no settings, so 'default' is not one.", error.Message);
    }

    private static FieldTypeRegistry CoreRegistry()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        return provider.GetRequiredService<FieldTypeRegistry>();
    }

    // Generic so that the plugin tests, which discover every field type in this assembly, skip it.
    private sealed class StubFieldType<TTag>(string name) : IFieldType
    {
        public string Name => name;

        public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
        {
            return [];
        }

        public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
        {
            return [];
        }

        public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
        {
            return new FieldValueDescription(new JsonObject(), null, "Anything.");
        }
    }
}
