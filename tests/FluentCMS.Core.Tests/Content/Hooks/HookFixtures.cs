using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Tests.Content.Validation;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Tests.Content.Hooks;

internal static class HookFixtures
{
    public static OperationContext Operation => ValidationFixtures.Operation;

    public static SchemaType ArticleType => ValidationFixtures.Article;

    public static HookPipeline Pipeline(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return new HookPipeline(services.BuildServiceProvider());
    }

    public static ValueTask RunAsync(
        HookPipeline pipeline,
        HookKind kind,
        ContentDocument entry,
        ContentDocument? previous = null,
        SchemaType? type = null) =>
        pipeline.RunAsync(kind, type ?? ArticleType, Operation, entry, previous, CancellationToken.None);

    // Maps the title and slug of an article, which is all the tests change.
    public sealed class ArticleMapper : IHookEntryMapper
    {
        public object ToEntry(Type clrType, ContentDocument document) => new Article
        {
            Title = document.Get<string>("title") ?? "",
            Slug = document.Get<string>("slug") ?? "",
        };

        public void CopyTo(object entry, ContentDocument document)
        {
            var article = (Article)entry;
            document["title"] = article.Title;
            document["slug"] = article.Slug;
        }
    }
}
