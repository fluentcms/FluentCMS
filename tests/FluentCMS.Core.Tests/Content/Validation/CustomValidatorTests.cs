using FluentCMS.Content;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Validation;

public class CustomValidatorTests
{
    private sealed class Seen
    {
        public List<(string? Field, string Path, object? Value, ValidationContext Context)> Fields { get; } = [];

        public List<(Article Entry, ValidationContext Context)> Entries { get; } = [];
    }

    private sealed class TitleValidator(Seen seen) : IFieldValidator<string>
    {
        public ValueTask ValidateAsync(string? value, ValidationContext context, CancellationToken cancellationToken)
        {
            seen.Fields.Add((context.Field?.Name, context.Path, value, context));
            if (context.Field?.Name == "title" && value == "Forbidden")
            {
                context.AddError("ACME0001", "That title is taken.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AnyFieldValidator(Seen seen) : IFieldValidator<object>
    {
        public ValueTask ValidateAsync(object? value, ValidationContext context, CancellationToken cancellationToken)
        {
            seen.Fields.Add((context.Field?.Name + "*", context.Path, value, context));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DateValidator(Seen seen) : IFieldValidator<DateTimeOffset>
    {
        public ValueTask ValidateAsync(DateTimeOffset value, ValidationContext context, CancellationToken cancellationToken)
        {
            seen.Fields.Add((context.Field?.Name + "@", context.Path, value, context));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ArticleValidator(Seen seen) : IEntryValidator<Article>
    {
        public ValueTask ValidateAsync(Article entry, ValidationContext context, CancellationToken cancellationToken)
        {
            seen.Entries.Add((entry, context));
            if (entry.Title.StartsWith("Draft:", StringComparison.Ordinal))
            {
                context.AddError("title", "ACME0002", "A title must not start with Draft:.");
                context.AddError("", "ACME0003", "The entry is not ready.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingValidator : IEntryValidator<Article>
    {
        public ValueTask ValidateAsync(Article entry, ValidationContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException("boom");
    }

    private sealed class BaseEntryValidator(Seen seen) : IEntryValidator<ContentItem>
    {
        public ValueTask ValidateAsync(ContentItem entry, ValidationContext context, CancellationToken cancellationToken)
        {
            seen.Entries.Add(((Article)entry, context));
            return ValueTask.CompletedTask;
        }
    }

    private static Action<IServiceCollection> Register(Seen seen) => services =>
    {
        services.AddSingleton(seen);
        services.AddSingleton<IFieldValidator<string>, TitleValidator>();
        services.AddSingleton<IFieldValidator<object>, AnyFieldValidator>();
        services.AddSingleton<IFieldValidator<DateTimeOffset>, DateValidator>();
        services.AddSingleton<IEntryValidator<Article>, ArticleValidator>();
    };

    [Fact]
    public async Task AFieldValidatorReceivesTheFullContext()
    {
        var seen = new Seen();
        var values = new Dictionary<string, object?> { ["title"] = "Hello", ["category"] = ArticleCategory.News };

        await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", values, ValidationMode.Publish, register: Register(seen));

        var (_, path, value, context) = seen.Fields.Single(item => item.Field == "title");
        path.ShouldBe("title");
        value.ShouldBe("Hello");
        context.Type.ShouldBeSameAs(ValidationFixtures.Article);
        context.Field.ShouldBeSameAs(ValidationFixtures.Article.FindField("title"));
        context.Values.ShouldBeSameAs(values);
        context.Operation.ShouldBeSameAs(ValidationFixtures.Operation);
        context.Operation.Principal.Name.ShouldBe("Amir");
        context.Operation.Locale.ShouldBe("fr-CA");
        context.Mode.ShouldBe(ValidationMode.Publish);
        context.Entry.ShouldBeNull();
    }

    [Fact]
    public async Task AFieldValidatorRejectsAFieldWithItsOwnCode()
    {
        var result = await ValidationFixtures.ValidateAsync(
            ValidationFixtures.Blog,
            "article",
            new() { ["title"] = "Forbidden" },
            ValidationMode.Draft,
            register: Register(new Seen()));

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ValidationError("title", "ACME0001", "That title is taken."));
    }

    [Fact]
    public async Task ValidatorsRunInDraftAndPublishAndSeeTheMode()
    {
        foreach (var mode in new[] { ValidationMode.Draft, ValidationMode.Publish })
        {
            var seen = new Seen();

            await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", new() { ["title"] = "T" }, mode, register: Register(seen));

            seen.Fields.ShouldAllBe(item => item.Context.Mode == mode);
            seen.Fields.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task AValidatorOfObjectSeesEveryVisibleFieldAndAValueTypeValidatorSkipsNull()
    {
        var seen = new Seen();
        var values = new Dictionary<string, object?> { ["title"] = "T", ["category"] = ArticleCategory.News };

        await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", values, ValidationMode.Publish, register: Register(seen));

        var visible = ValidationFixtures.Article.Fields.Where(field => field.Name != "videoUrl").Select(field => field.Name + "*");
        seen.Fields.Where(item => item.Field!.EndsWith('*')).Select(item => item.Field).ShouldBe(visible, "hidden videoUrl is skipped");
        seen.Fields.ShouldNotContain(item => item.Field == "publishedOn@");

        values["publishedOn"] = DateTimeOffset.UnixEpoch;
        seen.Fields.Clear();
        await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", values, ValidationMode.Publish, register: Register(seen));
        seen.Fields.ShouldContain(item => item.Field == "publishedOn@");
    }

    [Fact]
    public async Task ANestedFieldValidatorSeesItsSiblingsAndFullPath()
    {
        var seen = new Seen();
        var seo = new Dictionary<string, object?> { ["metaTitle"] = "Meta", ["metaDescription"] = "Desc" };

        await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", new() { ["title"] = "T", ["seo"] = seo }, ValidationMode.Publish, register: Register(seen));

        var (_, path, value, context) = seen.Fields.Single(item => item.Path == "seo.metaTitle" && item.Field == "metaTitle*");
        value.ShouldBe("Meta");
        context.Values.ShouldBeSameAs(seo);
        context.Type.ShouldBeSameAs(ValidationFixtures.Article);
    }

    [Fact]
    public async Task AnEntryValidatorReceivesTheEntryAndAddsErrorsAtAnyPath()
    {
        var seen = new Seen();
        var entry = new Article { Title = "Draft: x" };
        var values = new Dictionary<string, object?> { ["title"] = entry.Title };

        var result = await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", values, ValidationMode.Publish, entry, Register(seen));

        var (seenEntry, context) = seen.Entries.ShouldHaveSingleItem();
        seenEntry.ShouldBeSameAs(entry);
        context.Entry.ShouldBeSameAs(entry);
        context.Field.ShouldBeNull();
        context.Path.ShouldBe("");
        context.Values.ShouldBeSameAs(values);
        context.Operation.ShouldBeSameAs(ValidationFixtures.Operation);
        context.Mode.ShouldBe(ValidationMode.Publish);
        Should.Throw<InvalidOperationException>(() => context.AddError("X", "no field"));

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe([("title", "ACME0002"), ("", "ACME0003")]);
    }

    [Fact]
    public async Task AFieldValidatorSeesTheTypedEntryToo()
    {
        var seen = new Seen();
        var entry = new Article { Title = "T" };

        await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", new() { ["title"] = "T" }, ValidationMode.Publish, entry, Register(seen));

        seen.Fields.ShouldAllBe(item => ReferenceEquals(item.Context.Entry, entry));
    }

    [Fact]
    public async Task EntryValidatorsAlsoRunForTheBaseClassesOfTheEntry()
    {
        var seen = new Seen();

        await ValidationFixtures.ValidateAsync(
            ValidationFixtures.Blog,
            "article",
            new() { ["title"] = "T" },
            ValidationMode.Publish,
            new Article { Title = "T" },
            services => services.AddSingleton<IEntryValidator<ContentItem>>(new BaseEntryValidator(seen)));

        seen.Entries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EntryValidatorsDoNotRunWithoutAnEntry()
    {
        var seen = new Seen();

        await ValidationFixtures.ValidateAsync(ValidationFixtures.Blog, "article", new() { ["title"] = "T" }, ValidationMode.Publish, register: Register(seen));

        seen.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task ErrorsComeFromDeclarativeRulesThenFieldValidatorsThenEntryValidators()
    {
        var entry = new Article { Title = "Draft: x" };
        var values = new Dictionary<string, object?> { ["title"] = "Draft: x", ["category"] = ArticleCategory.Video };

        var result = await ValidationFixtures.ValidateAsync(
            ValidationFixtures.Blog,
            "article",
            values,
            ValidationMode.Publish,
            entry,
            services =>
            {
                services.AddSingleton<IFieldValidator<string>>(new RejectingValidator("title", "ACME0004"));
                services.AddSingleton<IEntryValidator<Article>>(new ArticleValidator(new Seen()));
            });

        ValidationFixtures.Describe(result).ShouldBe(["videoUrl|FCMS2110", "title|ACME0004", "title|ACME0002", "|ACME0003"]);
    }

    [Fact]
    public async Task ADefaultCodeIsAvailableForValidatorsWithoutOne()
    {
        var result = await ValidationFixtures.ValidateAsync(
            ValidationFixtures.Blog,
            "article",
            new() { ["title"] = "T" },
            ValidationMode.Draft,
            register: services => services.AddSingleton<IFieldValidator<string>>(new RejectingValidator("title", "")));

        result.Errors.Single().Code.ShouldBe(ValidationErrorCodes.Custom);
    }

    [Fact]
    public async Task AValidatorExceptionPropagates()
    {
        var thrown = await Should.ThrowAsync<NotSupportedException>(async () => await ValidationFixtures.ValidateAsync(
            ValidationFixtures.Blog,
            "article",
            new() { ["title"] = "T" },
            ValidationMode.Publish,
            new Article(),
            services => services.AddSingleton<IEntryValidator<Article>, ThrowingValidator>()));

        thrown.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task ACancelledTokenStopsValidation()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var pipeline = ValidationFixtures.Pipeline(ValidationFixtures.Blog, services => services.AddSingleton<IFieldValidator<string>>(new RejectingValidator("title", "X")));

        await Should.ThrowAsync<OperationCanceledException>(async () => await pipeline.ValidateAsync(
            ValidationFixtures.Article,
            new Dictionary<string, object?> { ["title"] = "T" },
            null,
            ValidationFixtures.Operation,
            ValidationMode.Draft,
            source.Token));
    }

    [Fact]
    public async Task AnEntryWithNoProblemsIsTheSharedSuccess()
    {
        var result = await ValidationFixtures.ValidateArticleAsync(new() { ["title"] = "T" }, ValidationMode.Publish);

        result.ShouldBeSameAs(ValidationResult.Success);
    }

    private sealed class RejectingValidator(string field, string code) : IFieldValidator<string>
    {
        public ValueTask ValidateAsync(string? value, ValidationContext context, CancellationToken cancellationToken)
        {
            if (context.Field?.Name == field)
            {
                context.AddError(code, "rejected");
            }

            return ValueTask.CompletedTask;
        }
    }
}
