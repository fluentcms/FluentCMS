using System.Text.Json.Nodes;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Samples.Blog;

// The welcome article of the seed and what it refers to, as the API writes them, for the examples of the OpenAPI
// document. The ids are fixed, since a seeded instance makes its own.
public static class BlogExamples
{
    private const string AdaId = "0193a5f2-6c1e-7d3a-9b1f-2a4c6e8f0a01";
    private const string DotnetId = "0193a5f2-6c1e-7d3a-9b1f-2a4c6e8f0b01";
    private const string HeadlessId = "0193a5f2-6c1e-7d3a-9b1f-2a4c6e8f0b02";
    private const string WelcomeId = "0193a5f2-6c1e-7d3a-9b1f-2a4c6e8f0c01";
    private const string Moment = "2026-01-15T09:00:00Z";

    public static JsonObject Hero => new() { ["type"] = "hero", ["heading"] = "Welcome", ["subheading"] = "A headless CMS for .NET" };

    public static JsonObject Quote => new() { ["type"] = "quote", ["text"] = "Content is data first.", ["attribution"] = "The FluentCMS team" };

    public static JsonObject Gallery => new() { ["type"] = "gallery", ["caption"] = "Screenshots", ["columns"] = 2 };

    public static JsonObject Seo => new() { ["metaTitle"] = "Welcome to FluentCMS", ["metaDescription"] = "What FluentCMS is and who it is for." };

    public static JsonObject Article => Entry(WelcomeId, "en", new()
    {
        ["title"] = "Welcome to FluentCMS",
        ["slug"] = BlogSeeder.WelcomeSlug,
        ["body"] = Text("FluentCMS is a headless CMS for .NET."),
        ["author"] = AdaId,
        ["tags"] = new JsonArray(DotnetId, HeadlessId),
        ["sections"] = new JsonArray(Hero, Quote),
        ["seo"] = Seo,
        ["category"] = "news",
        ["publishedOn"] = Moment,
        ["videoUrl"] = null,
    });

    public static JsonObject Author => Entry(AdaId, "", new()
    {
        ["name"] = "Ada Lovelace",
        ["slug"] = BlogSeeder.AdaSlug,
        ["bio"] = Text("Writes about analytical engines and typed content."),
    });

    public static JsonObject Tag => Entry(DotnetId, "", new() { ["name"] = "dotnet", ["slug"] = BlogSeeder.DotnetSlug });

    public static FluentCmsBuilder AddBlogExamples(this FluentCmsBuilder cms)
    {
        ArgumentNullException.ThrowIfNull(cms);

        cms.OpenApi
            .AddExample<Article>(Article)
            .AddExample<Author>(Author)
            .AddExample<Tag>(Tag)
            .AddExample<Seo>(Seo)
            .AddExample<HeroBlock>(Hero)
            .AddExample<QuoteBlock>(Quote)
            .AddExample<GalleryBlock>(Gallery);
        return cms;
    }

    private static JsonObject Entry(string id, string locale, JsonObject fields)
    {
        JsonObject entry = new()
        {
            ["id"] = id,
            ["status"] = "published",
            ["locale"] = locale,
            ["version"] = 1,
            ["createdAt"] = Moment,
            ["updatedAt"] = Moment,
            ["publishedAt"] = Moment,
        };
        foreach (var (name, value) in fields.ToList())
        {
            fields.Remove(name);
            entry[name] = value;
        }

        return entry;
    }

    private static JsonObject Text(string text) => new()
    {
        ["type"] = "doc",
        ["content"] = new JsonArray(new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        }),
    };
}
