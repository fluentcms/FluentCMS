using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Samples.Blog;

public static class BlogSample
{
    public const string SecondLocale = "fr";

    public static IReadOnlyList<Type> ContentTypes { get; } =
    [
        typeof(Article),
        typeof(Author),
        typeof(Tag),
        typeof(Seo),
        typeof(HeroBlock),
        typeof(QuoteBlock),
        typeof(GalleryBlock),
    ];

    // Registers the settings, the seed set and the OpenAPI examples. The content types still go to the schema, from ContentTypes.
    public static FluentCmsBuilder AddBlogSample(this FluentCmsBuilder cms)
    {
        ArgumentNullException.ThrowIfNull(cms);

        cms.Settings.Add<BlogSettings>();
        cms.Seeding.RequireLocale(SecondLocale).Add<BlogSeeder>();
        return cms.AddBlogExamples();
    }
}
