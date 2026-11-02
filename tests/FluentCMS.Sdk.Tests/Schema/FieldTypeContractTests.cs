using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class FieldTypeContractTests
{
    [Fact]
    public void FilterOperatorsMatchTheQueryGrammarOneToOne()
    {
        Enum.GetNames<FilterOperator>().ShouldBe(
        [
            "Equal", "NotEqual", "In", "NotIn", "LessThan", "LessThanOrEqual", "GreaterThan", "GreaterThanOrEqual",
            "Between", "Contains", "StartsWith", "IsNull", "IsNotNull", "HasBlock",
        ]);
    }

    [Fact]
    public void StorageKindsAreColumnJsonAndReference()
    {
        Enum.GetNames<FieldStorageKind>().ShouldBe(["Column", "Json", "Reference"]);
    }

    [Fact]
    public void SettingKindsAreTheValuesASchemaCanHold()
    {
        Enum.GetNames<FieldSettingKind>().ShouldBe(["String", "Boolean", "Integer", "Decimal", "StringList"]);
    }

    [Fact]
    public void ASettingIsOptionalByDefaultAndEqualByValue()
    {
        var setting = new FieldSettingDescriptor("scale", FieldSettingKind.Integer);

        setting.Required.ShouldBeFalse();
        setting.ShouldBe(new FieldSettingDescriptor("scale", FieldSettingKind.Integer, false));
        setting.ShouldNotBe(new FieldSettingDescriptor("scale", FieldSettingKind.Integer, true));
    }

    [Fact]
    public void AFieldTypeRejectsUnlistedSettingsUnlessItSaysOtherwise()
    {
        IFieldType fieldType = new MinimalFieldType();

        fieldType.AcceptsAnySetting.ShouldBeFalse();
    }

    private sealed class MinimalFieldType : IFieldType
    {
        public string Name => "minimal";

        public IReadOnlyList<Type> ClrTypes => [typeof(string)];

        public IReadOnlySet<FilterOperator> FilterOperators => new HashSet<FilterOperator>();

        public bool Sortable => false;

        public bool Indexable => false;

        public IReadOnlyList<FieldSettingDescriptor> Settings => [];

        public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

        public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
        {
            result = value;
            message = null;
            return true;
        }

        public JsonObject OpenApiSchema(SchemaField field) => [];
    }
}
