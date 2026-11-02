using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class PluginFieldStorageTests
{
    private static readonly Guid Site = Guid.CreateVersion7();

    private static SchemaModel Schema(Action<IFieldBuilder> color, Action<IFieldBuilder> palette) =>
        new SchemaBuilder(new FieldTypeRegistry([new ColorFieldType(), new PaletteFieldType()]))
            .AddCollection("theme", type => type.Field("name", "text").Field("accent", "color", color).Field("swatches", "palette", palette))
            .Build();

    [Fact]
    public void ColumnTypeRoundTripsARow()
    {
        using var database = new StorageDatabase(Schema(_ => { }, _ => { }));
        var documentId = Guid.CreateVersion7();

        using (var context = database.CreateCreatedContext())
        {
            context.Rows("cms_theme").Add(new Dictionary<string, object>
            {
                ["document_id"] = documentId,
                ["site_id"] = Site,
                ["state"] = ContentState.Draft,
                ["name"] = "Dark",
                ["accent"] = "#ff8800",
            });
            context.SaveChanges();
        }

        using (var context = database.CreateContext())
        {
            var row = context.Rows("cms_theme").Single(row => EF.Property<Guid>(row, "document_id") == documentId);
            row["accent"].ShouldBe("#ff8800");
            row["swatches"].ShouldBe(null!);
        }
    }

    [Fact]
    public void JsonTypeRoundTripsARow()
    {
        using var database = new StorageDatabase(Schema(_ => { }, _ => { }));
        var documentId = Guid.CreateVersion7();
        const string swatches = """["#000","#fff"]""";

        using (var context = database.CreateCreatedContext())
        {
            context.Rows("cms_theme").Add(new Dictionary<string, object>
            {
                ["document_id"] = documentId,
                ["site_id"] = Site,
                ["state"] = ContentState.Draft,
                ["swatches"] = swatches,
            });
            context.SaveChanges();
        }

        using (var context = database.CreateContext())
        {
            context.Rows("cms_theme").Single(row => EF.Property<Guid>(row, "document_id") == documentId)["swatches"].ShouldBe(swatches);
        }
    }

    [Fact]
    public void PluginColumnIsANullableStringAndHonoursMaxLength()
    {
        var schema = Schema(field => field.Length(null, 7), _ => { });

        var accent = FieldStorage.Of(schema, schema.FindContentType("theme")!).Single(field => field.Column == "accent");

        accent.Kind.ShouldBe(FieldStorageKind.Column);
        accent.ClrType.ShouldBe(typeof(string));
        accent.Required.ShouldBeFalse();
        accent.MaxLength.ShouldBe(7);
    }

    [Fact]
    public void PluginJsonIsANullableJsonString()
    {
        var schema = Schema(_ => { }, _ => { });

        var swatches = FieldStorage.Of(schema, schema.FindContentType("theme")!).Single(field => field.Column == "swatches");

        swatches.Kind.ShouldBe(FieldStorageKind.Json);
        swatches.ClrType.ShouldBe(typeof(string));
        swatches.Required.ShouldBeFalse();
        swatches.DefaultValue.ShouldBeNull();
    }

    [Fact]
    public void PluginJsonListIsRequiredWithAnEmptyDefault()
    {
        var schema = Schema(_ => { }, field => field.Multiple());

        var swatches = FieldStorage.Of(schema, schema.FindContentType("theme")!).Single(field => field.Column == "swatches");

        swatches.Required.ShouldBeTrue();
        swatches.DefaultValue.ShouldBe("[]");
    }

    [Fact]
    public void ContentModelBuilderRaisesNoSchemaErrorsOfItsOwn()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "FluentCMS.Core", "Storage", "ContentModelBuilder.cs"));

        source.ShouldNotContain("SchemaValidationException");
        source.ShouldNotContain("SchemaError");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
