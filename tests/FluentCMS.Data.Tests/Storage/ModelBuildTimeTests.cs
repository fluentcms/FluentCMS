using System.Diagnostics;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class ModelBuildTimeTests
{
    private const int TypeCount = 50;

    private const int Samples = 3;

    private static readonly string[] FieldTypes =
        ["text", "longText", "richText", "integer", "decimal", "boolean", "date", "dateTime", "email", "url", "json"];

    // Half the types are localized, with half of their fields in the locale table: 75 tables in all. Each variant has its own
    // type names, so that EF builds its model instead of taking it from its cache.
    private static SchemaModel LargeSchema(int variant)
    {
        var builder = SampleSchema.NewBuilder();
        for (var i = 0; i < TypeCount; i++)
        {
            var localized = i % 2 == 0;
            builder.AddCollection($"type-{variant}-{i}", type =>
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

    private static TimeSpan Build(SchemaModel schema)
    {
        using var database = new StorageDatabase(schema);
        using var context = database.CreateContext();

        var watch = Stopwatch.StartNew();
        var model = context.Model;
        watch.Stop();

        model.GetEntityTypes().Count().ShouldBe(TypeCount + (TypeCount / 2) + 30);
        return watch.Elapsed;
    }

    // The target is one second. A shared runner is too noisy for a bound on it, so the times are reported, not asserted;
    // each build has to map every table.
    [Fact]
    public void FiftyTypesBuildAModelOfEveryTable()
    {
        var warmUp = Build(LargeSchema(0));
        var builds = Enumerable.Range(1, Samples).Select(variant => Build(LargeSchema(variant))).ToList();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Model of {TypeCount} types built in {warmUp.TotalMilliseconds:F0} ms cold, then {string.Join(", ", builds.Select(build => $"{build.TotalMilliseconds:F0}"))} ms");
    }
}
