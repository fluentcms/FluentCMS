using System.Diagnostics;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class ModelBuildTimeTests
{
    private const int TypeCount = 50;

    // The target is one second; the bound leaves room for a slow CI machine.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(3);

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
    public void FiftyTypesBuildWithinTheBound()
    {
        var schema = LargeSchema();
        using var database = new StorageDatabase(schema);
        using var context = database.CreateContext();

        var watch = Stopwatch.StartNew();
        var model = context.Model;
        watch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"Model of {TypeCount} types built in {watch.ElapsedMilliseconds} ms");
        model.GetEntityTypes().Count().ShouldBe(TypeCount + (TypeCount / 2) + 11);
        watch.Elapsed.ShouldBeLessThan(Bound);
    }
}
