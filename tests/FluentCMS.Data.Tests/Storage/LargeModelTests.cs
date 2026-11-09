using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class LargeModelTests
{
    private const int TypeCount = 50;

    private static readonly string[] FieldTypes =
        ["text", "longText", "richText", "integer", "decimal", "boolean", "date", "dateTime", "email", "url", "json"];

    // Half the types are localized, with half of their fields in the locale table: 75 tables in all.
    private static SchemaModel LargeSchema()
    {
        var builder = SampleSchema.NewBuilder();
        for (var i = 0; i < TypeCount; i++)
        {
            var localized = i % 2 == 0;
            builder.AddCollection($"type-{i}", type =>
            {
                type.Localized(localized);
                for (var f = 0; f < 20; f++)
                {
                    var localizable = localized && f % 2 == 0;
                    var indexable = FieldTypes[f % FieldTypes.Length] is "text" or "integer" or "dateTime";
                    type.Field($"field{f}", FieldTypes[f % FieldTypes.Length], field => field.Localizable(localizable).Indexed(indexable));
                }
            });
        }

        return builder.Build();
    }

    [Fact]
    public void FiftyTypesBuildAModelOfEveryTable()
    {
        using var database = new StorageDatabase(LargeSchema());
        using var context = database.CreateContext();

        context.Model.GetEntityTypes().Count().ShouldBe(TypeCount + (TypeCount / 2) + 30);
    }
}
