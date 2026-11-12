using FluentCMS.Core.DependencyInjection;

namespace FluentCmsApp.Blog;

public static class BlogSample
{
    public const string SecondLocale = "fr";

    // The settings area and the seed set of the blog. The seeder runs once per version, at the first start or with
    // `fluentcms seed`, after the default site allows the second locale.
    public static FluentCmsBuilder AddBlogSample(this FluentCmsBuilder cms)
    {
        ArgumentNullException.ThrowIfNull(cms);

        cms.Settings.Add<BlogSettings>();
        cms.Seeding.RequireLocale(SecondLocale).Add<BlogSeeder>();
        return cms;
    }
}
