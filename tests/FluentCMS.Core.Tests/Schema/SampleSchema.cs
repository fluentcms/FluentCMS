using System.Reflection;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Tests.Schema.FieldTypes;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema;

internal static class SampleSchema
{
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "Schema", "Samples", "blog.schema.json");

    // The media plugin registers the media field type; here a stand-in descriptor is registered in its place, beside a plugin type that holds lists.
    public static SchemaBuilder NewBuilder() => new(new FieldTypeRegistry([new MediaFieldType(), new ListFieldType()]));

    public static SchemaBuilder FromClasses() => NewBuilder()
        .AddContentType<Article>()
        .AddContentType<Author>()
        .AddContentType<Tag>()
        .AddContentType<Seo>()
        .AddContentType<HeroBlock>()
        .AddContentType<SiteSettings>();

    public static SchemaBuilder FromFile() => NewBuilder().LoadSchemaFile(FilePath);

    public static string Canonical(SchemaBuilder builder) => SchemaJsonWriter.Write(builder.Build());

    // Limits an assembly to the sample types, so the invalid classes of other tests are not scanned.
    public static Assembly SamplesOnly() => new FilteredAssembly(
        typeof(Article).Assembly,
        type => type.Namespace == typeof(Article).Namespace);

    private sealed class FilteredAssembly(Assembly inner, Func<Type, bool> include) : Assembly
    {
        public override Type[] GetTypes() => [.. inner.GetTypes().Where(include)];
    }
}
