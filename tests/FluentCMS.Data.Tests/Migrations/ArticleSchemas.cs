using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;

namespace FluentCMS.Data.Tests.Migrations;

internal static class ArticleSchemas
{
    public static readonly SchemaModel NoTypes = SampleSchema.NewBuilder().Build();

    public static readonly SchemaModel Titled = Article("title");

    public static readonly SchemaModel WithSubtitle = Article("title", "subtitle");

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
