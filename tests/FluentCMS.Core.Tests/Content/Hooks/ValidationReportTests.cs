using FluentCMS.Content;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Tests.Content.Validation;
using FluentCMS.Core.Tests.Schema.Samples;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Hooks;

public class ValidationReportTests
{
    private sealed class SlugValidator : IFieldValidator<string>
    {
        public ValueTask ValidateAsync(string? value, ValidationContext context, CancellationToken cancellationToken)
        {
            if (context.Field?.Name == "slug" && value == "admin")
            {
                context.AddError("ACME0001", "That slug is reserved.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ArticleValidator : IEntryValidator<Article>
    {
        public ValueTask ValidateAsync(Article entry, ValidationContext context, CancellationToken cancellationToken)
        {
            context.AddError("", "ACME0002", "The entry is not ready.");
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task OneResultListsEveryFailingFieldAcrossDeclarativeAndCustomValidators()
    {
        var values = new Dictionary<string, object?>
        {
            ["title"] = " ",
            ["slug"] = "admin",
            ["category"] = ArticleCategory.Video,
            ["seo"] = new Dictionary<string, object?> { ["metaTitle"] = new string('x', 500) },
        };

        var result = await ValidationFixtures.ValidateAsync(
            ValidationFixtures.Blog,
            "article",
            values,
            ValidationMode.Publish,
            entry: new Article(),
            register: services =>
            {
                services.AddSingleton<IFieldValidator<string>, SlugValidator>();
                services.AddSingleton<IEntryValidator<Article>, ArticleValidator>();
            });

        var errors = ValidationFixtures.Describe(result);
        errors.ShouldContain("title|FCMS2101");
        errors.ShouldContain("videoUrl|FCMS2110");
        errors.ShouldContain("slug|ACME0001");
        errors.ShouldContain("|ACME0002");
        errors.Count(error => error.StartsWith("seo.", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(1);
        errors.Distinct().Count().ShouldBe(errors.Length);
    }
}
