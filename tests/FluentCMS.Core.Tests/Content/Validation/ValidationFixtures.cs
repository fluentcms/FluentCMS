using FluentCMS.Content;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Tests.Content.Validation;

internal static class ValidationFixtures
{
    public static OperationContext Operation { get; } = new(
        new Principal(PrincipalKind.AdminUser, Guid.Parse("00000000-0000-0000-0000-000000000001"), "Amir"),
        Origin.Admin,
        new SiteId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
        "fr-CA",
        "admin-client",
        "corr-1",
        new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    public static SchemaModel Blog { get; } = SampleSchema.FromClasses().Build();

    public static SchemaType Article => Blog.FindContentType("article")!;

    public static ValidationPipeline Pipeline(SchemaModel model, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        register?.Invoke(services);
        return new ValidationPipeline(model, services.BuildServiceProvider());
    }

    public static ValueTask<ValidationResult> ValidateAsync(
        SchemaModel model,
        string typeName,
        Dictionary<string, object?> values,
        ValidationMode mode,
        object? entry = null,
        Action<IServiceCollection>? register = null) =>
        Pipeline(model, register).ValidateAsync(model.FindContentType(typeName)!, values, entry, Operation, mode, CancellationToken.None);

    public static ValueTask<ValidationResult> ValidateArticleAsync(Dictionary<string, object?> values, ValidationMode mode) =>
        ValidateAsync(Blog, "article", values, mode);

    // A schema of one content type "thing", with the components and blocks the test adds.
    public static SchemaModel Model(Action<ITypeBuilder> thing, Action<SchemaBuilder>? others = null)
    {
        var builder = new SchemaBuilder().AddCollection("thing", thing);
        others?.Invoke(builder);
        return builder.Build();
    }

    public static string[] Describe(ValidationResult result) =>
        [.. result.Errors.Select(error => $"{error.Path}|{error.Code}")];
}
