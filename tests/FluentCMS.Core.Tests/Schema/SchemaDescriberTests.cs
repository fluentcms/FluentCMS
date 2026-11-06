using FluentCMS.Core.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema;

// What the Blog sample's stored introspection document does not hold: display hints, defaults, ranges, patterns and
// conditions that combine others.
public class SchemaDescriberTests
{
    private const string Json = """
        {
          "collections": [
            {
              "name": "product",
              "localized": true,
              "scope": "global",
              "unpublishedReferences": "block",
              "maxVersions": 5,
              "fields": [
                { "name": "category", "type": "select", "options": ["news", "video"] },
                { "name": "rating", "type": "decimal", "minimum": 0, "maximum": 5.5, "precision": 4, "scale": 1, "defaultValue": 1.5 },
                {
                  "name": "code",
                  "type": "text",
                  "minLength": 2,
                  "maxLength": 8,
                  "pattern": "^[A-Z]+$",
                  "required": true,
                  "readOnly": true,
                  "hidden": true,
                  "localizable": true,
                  "formerNames": ["sku"],
                  "defaultValue": "AB",
                  "display": { "label": "Code", "help": "Help", "placeholder": "AB", "group": "Main", "tab": "Info", "width": 6 },
                  "visibleWhen": { "and": [ { "field": "category", "in": ["news", null] }, { "not": { "field": "rating", "isEmpty": true } } ] },
                  "requiredWhen": { "or": [ { "field": "category", "equals": "video" }, { "field": "rating", "equals": 3 } ] }
                }
              ]
            }
          ],
          "components": [ { "name": "address", "fields": [ { "name": "city", "type": "text" } ] } ]
        }
        """;

    private static readonly SchemaModel Model = new SchemaBuilder().AddSchemaJson(Json).Build();

    [Fact]
    public void MetadataAndValidationAreCarriedOver()
    {
        var product = SchemaDescriber.Describe(Model.FindContentType("product")!);

        product.Kind.ShouldBe(SchemaTypeKind.Collection);
        product.Localized.ShouldBeTrue();
        product.Scope.ShouldBe(ContentScope.Global);
        product.UnpublishedReferences.ShouldBe(UnpublishedReferencePolicy.Block);
        product.MaxVersions.ShouldBe(5);

        var code = product.Fields.Single(field => field.Name == "code");
        code.ReadOnly.ShouldBeTrue();
        code.Hidden.ShouldBeTrue();
        code.Localizable.ShouldBeTrue();
        code.FormerNames.ShouldBe(["sku"]);
        code.DefaultValue.ShouldBe("AB");
        code.Display.ShouldBe(new FieldDisplayDescription { Label = "Code", Help = "Help", Placeholder = "AB", Group = "Main", Tab = "Info", Width = 6 });
        code.Validation.ShouldBe(new FieldValidationDescription { Required = true, MinLength = 2, MaxLength = 8, Pattern = "^[A-Z]+$" });

        var rating = product.Fields.Single(field => field.Name == "rating");
        rating.Validation.Minimum.ShouldBe(0m);
        rating.Validation.Maximum.ShouldBe(5.5m);
        rating.DefaultValue.ShouldBe(1.5m);
        rating.Settings.Keys.ShouldBe(["precision", "scale"]);
    }

    [Fact]
    public void ConditionsAreDataThatNestAsTheModelDoes()
    {
        var code = SchemaDescriber.Describe(Model.FindContentType("product")!).Fields.Single(field => field.Name == "code");

        var visible = code.VisibleWhen!;
        visible.Operator.ShouldBe(ConditionOperator.And);
        visible.Field.ShouldBeNull();
        visible.Values.ShouldBeNull();
        visible.Operands!.Count.ShouldBe(2);
        visible.Operands[0].Operator.ShouldBe(ConditionOperator.In);
        visible.Operands[0].Field.ShouldBe("category");
        visible.Operands[0].Values.ShouldBe(["news", null]);
        visible.Operands[1].Operator.ShouldBe(ConditionOperator.Not);
        var empty = visible.Operands[1].Operands.ShouldHaveSingleItem();
        empty.Operator.ShouldBe(ConditionOperator.IsEmpty);
        empty.Field.ShouldBe("rating");
        empty.Values.ShouldBeNull();
        empty.Operands.ShouldBeNull();

        var required = code.RequiredWhen!;
        required.Operator.ShouldBe(ConditionOperator.Or);
        required.Operands!.Select(operand => operand.Values!.Single()).ShouldBe(["video", 3L]);
    }

    [Fact]
    public void AComponentHasNoPublishingPolicy()
    {
        var address = SchemaDescriber.Describe(Model).Components.ShouldHaveSingleItem();

        address.Kind.ShouldBe(SchemaTypeKind.Component);
        address.Scope.ShouldBeNull();
        address.UnpublishedReferences.ShouldBeNull();
        address.MaxVersions.ShouldBeNull();
        address.TrashRetentionDays.ShouldBeNull();
        address.ReverseRelations.ShouldBeEmpty();
    }
}
