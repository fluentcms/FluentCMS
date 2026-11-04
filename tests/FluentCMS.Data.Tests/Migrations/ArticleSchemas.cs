using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;

namespace FluentCMS.Data.Tests.Migrations;

internal static class ArticleSchemas
{
    public static readonly SchemaModel NoTypes = SampleSchema.NewBuilder().Build();

    public static readonly SchemaModel Titled = Article("title");

    public static readonly SchemaModel WithSubtitle = Article("title", "subtitle");

    // Differs from Titled in what the database does not store, so a migration has nothing to change.
    public static readonly SchemaModel TitledAndRequired = SampleSchema.NewBuilder()
        .AddCollection("article", type => type.Field("title", "text", field => field.Required()))
        .Build();

    public static SchemaModel Article(params string[] textFields) =>
        SampleSchema.NewBuilder()
            .AddCollection("article", type =>
            {
                foreach (var field in textFields)
                {
                    type.Field(field, "text");
                }
            })
            .Build();
}
