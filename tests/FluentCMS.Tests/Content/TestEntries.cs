using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Content;

internal static class TestEntries
{
    public const string ProductId = "0199a000-0000-7000-8000-000000000001";

    public static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    public static FieldTypeRegistry Registry { get; } = BuildRegistry();

    public static ContentTypeDefinition Page { get; } = new(
    [
        Field("headline", "text", """{"maxLength": 80}""", required: true),
        Field("body", "richText"),
        Field("rating", "number", """{"integer": true, "min": 1, "max": 5}"""),
        Field("tags", "list", """{"itemType": "text"}"""),
        Field("product", "reference", """{"target": "product"}"""),
    ]);

    public static ContentTypeDefinition Product { get; } = new(
        [Field("name", "text", required: true), Field("price", "number", """{"min": 0}""", required: true), Field("launched", "dateTime")],
        isEntity: true);

    public static ContentType Type(ContentTypeDefinition definition, string slug = "page")
    {
        return ContentType.Create(Site.Default.Id, slug, slug, definition, Registry).Value;
    }

    public static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public static FieldDefinition Field(string name, string type, string settings = "{}", bool required = false)
    {
        return new FieldDefinition(name, type, required, Json(settings));
    }

    private static FieldTypeRegistry BuildRegistry()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        return provider.GetRequiredService<FieldTypeRegistry>();
    }
}
