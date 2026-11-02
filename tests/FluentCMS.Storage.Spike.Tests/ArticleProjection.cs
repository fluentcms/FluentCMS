using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Storage.Spike.Tests;

// The user's typed class in the spec's example: a plain projection with no table of its own.
public sealed class Article
{
    public Guid DocumentId { get; set; }

    public int State { get; set; }

    public string? Locale { get; set; }

    public string? Status { get; set; }

    public int Version { get; set; }

    public string? Title { get; set; }

    public string? Slug { get; set; }

    public string? Category { get; set; }

    public string? Sections { get; set; }

    public DateTimeOffset? PublishedOn { get; set; }
}

internal sealed class ArticleRow
{
    public Dictionary<string, object> Shared { get; set; } = null!;

    public Dictionary<string, object> Locale { get; set; } = null!;
}

internal static class ArticleProjection
{
    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly Dictionary<string, (string Source, string Column)> Map = new()
    {
        [nameof(Article.DocumentId)] = (nameof(ArticleRow.Shared), "document_id"),
        [nameof(Article.State)] = (nameof(ArticleRow.Shared), "state"),
        [nameof(Article.Locale)] = (nameof(ArticleRow.Locale), "locale"),
        [nameof(Article.Title)] = (nameof(ArticleRow.Locale), "title"),
        [nameof(Article.Slug)] = (nameof(ArticleRow.Locale), "slug"),
        [nameof(Article.Category)] = (nameof(ArticleRow.Shared), "category"),
        [nameof(Article.Sections)] = (nameof(ArticleRow.Shared), "sections"),
        [nameof(Article.PublishedOn)] = (nameof(ArticleRow.Shared), "published_on"),
    };

    public static IQueryable<ArticleRow> Joined(SpikeDbContext context) =>
        context.Rows("cms_article").Join(
            context.Rows("cms_article_i18n"),
            a => new { DocumentId = EF.Property<Guid>(a, "document_id"), State = EF.Property<int>(a, "state") },
            t => new { DocumentId = EF.Property<Guid>(t, "document_id"), State = EF.Property<int>(t, "state") },
            (a, t) => new ArticleRow { Shared = a, Locale = t });

    // What a reflection-based mapper would produce from the schema and a `fields=` selection.
    public static Expression<Func<ArticleRow, Article>> Select(params string[] properties)
    {
        var row = Expression.Parameter(typeof(ArticleRow), "row");
        var bindings = properties.Select(name =>
        {
            var property = typeof(Article).GetProperty(name)!;
            var (source, column) = Map[name];
            var value = Expression.Call(
                EfProperty.MakeGenericMethod(property.PropertyType),
                Expression.Property(row, source),
                Expression.Constant(column));
            return (MemberBinding)Expression.Bind(property, value);
        });

        return Expression.Lambda<Func<ArticleRow, Article>>(Expression.MemberInit(Expression.New(typeof(Article)), bindings), row);
    }
}
