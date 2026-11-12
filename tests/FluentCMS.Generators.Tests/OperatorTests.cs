using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Generators.Tests.Kitchen;
using FluentCMS.Plugins.Media;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Generators.Tests;

// Every field the generator emitted for the Kitchen types, which have a field of every type at every level a filter
// reaches, is held to the content service: each filter and sort a builder gives is accepted, and every operator and
// sort a builder leaves out is refused.
public sealed class OperatorTests : IAsyncLifetime
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-generators-media-");
    private FluentCmsTestHost _host = null!;
    private IContentService<Kitchen.Kitchen> _kitchens = null!;

    public async ValueTask InitializeAsync()
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes(typeof(Kitchen.Kitchen), typeof(Shelf), typeof(Bin), typeof(Recipe), typeof(Note))
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = _media.FullName));
        var user = await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        _kitchens = _host.As(user).Content<Kitchen.Kitchen>();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _media.Delete(recursive: true);
    }

    [Fact]
    public async Task AFieldOfEachKindHasOnlyTheOperatorsItsKindSupports()
    {
        var fields = Fields().ToList();
        fields.Select(field => field.Path).ShouldContain("menu.recipe.cooked");
        fields.Select(field => field.Path).ShouldContain("shelf.bin.full");
        fields.Select(field => field.Path).ShouldContain("metadata.audience");

        List<string> problems = [];
        foreach (var field in fields)
        {
            var given = Filters(field).ToList();
            foreach (var filter in given)
            {
                if (await Refusal(new ContentQuery { Filter = filter }) is { } refusal)
                {
                    problems.Add($"{field.Path} {filter.Operator} was refused: {refusal}");
                }
            }

            var operand = given.Select(filter => filter.Value).FirstOrDefault(value => value is not null and not System.Collections.IEnumerable or string) ?? "x";
            foreach (var missing in Enum.GetValues<FilterOperator>().Except(given.Select(filter => filter.Operator)))
            {
                if (await Refusal(new ContentQuery { Filter = new FieldFilter(field.Path, missing, Operand(missing, operand)) }) is null)
                {
                    problems.Add($"{field.Path} {missing} is accepted but the builder has no member for it");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASortBuilderExistsForEverySortableField()
    {
        List<string> problems = [];
        foreach (var field in Fields())
        {
            var ascending = field.GetType().GetMethod("Ascending");
            var refusal = await Refusal(new ContentQuery { Sort = [new SortField(field.Path)] });
            if (ascending is not null && refusal is not null)
            {
                problems.Add($"{field.Path} has a sort builder but is not sortable: {refusal}");
            }
            else if (ascending is null && refusal is null)
            {
                problems.Add($"{field.Path} is sortable but has no sort builder");
            }
            else if (ascending is not null)
            {
                ascending.Invoke(field, null).ShouldBe(new SortField(field.Path));
                field.GetType().GetMethod("Descending")!.Invoke(field, null).ShouldBe(new SortField(field.Path, Descending: true));
            }
        }

        problems.ShouldBeEmpty();
    }

    // Every QueryField the builders of the kitchen reach: through components, block lists and their block types, and a key of the JSON field.
    private static IEnumerable<QueryField> Fields()
    {
        var pending = new Queue<object?>(typeof(KitchenFields).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(property => property.GetValue(null)));
        while (pending.TryDequeue(out var item))
        {
            if (item is QueryField field)
            {
                yield return field;
                if (field is JsonField json)
                {
                    yield return json.Key("audience");
                }
            }

            if (item is not null and not JsonField && item.GetType().Namespace != typeof(QueryField).Namespace)
            {
                foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.Name != nameof(QueryField.Path)))
                {
                    pending.Enqueue(property.GetValue(item));
                }
            }
        }
    }

    private static IEnumerable<FieldFilter> Filters(QueryField field) =>
        field.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.ReturnType == typeof(FieldFilter))
            .Select(method => (FieldFilter)method.Invoke(field, [.. method.GetParameters().Select(parameter => Sample(parameter.ParameterType, method.Name, field.Path))])!);

    // An email or URL field coerces its operand, so it gets one of its form.
    private static object Sample(Type type, string method, string path)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var element = type.GetGenericArguments()[0];
            var list = Array.CreateInstance(element, 1);
            list.SetValue(Sample(element, method, path), 0);
            return list;
        }

        return type switch
        {
            _ when type == typeof(string) && method == "HasBlock" => "recipe",
            _ when type == typeof(string) && (path.EndsWith("contact", StringComparison.Ordinal) || path.EndsWith("chef", StringComparison.Ordinal)) => "cook@example.test",
            _ when type == typeof(string) && (path.EndsWith("website", StringComparison.Ordinal) || path.EndsWith("source", StringComparison.Ordinal)) => "https://example.test/",
            _ when type == typeof(string) => "x",
            _ when type == typeof(long) => 1L,
            _ when type == typeof(decimal) => 1.5m,
            _ when type == typeof(bool) => true,
            _ when type == typeof(DateOnly) => new DateOnly(2026, 11, 12),
            _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero),
            _ when type == typeof(Guid) => Guid.NewGuid(),
            _ => throw new InvalidOperationException($"No sample for {type}"),
        };
    }

    private static object? Operand(FilterOperator @operator, object value) => @operator switch
    {
        FilterOperator.IsNull or FilterOperator.IsNotNull => null,
        FilterOperator.In or FilterOperator.NotIn => new[] { value },
        FilterOperator.Between => new[] { value, value },
        _ => value,
    };

    private async Task<string?> Refusal(ContentQuery query)
    {
        try
        {
            await _kitchens.QueryAsync(query with { Status = QueryStatus.Draft }, Token);
            return null;
        }
        catch (ValidationException exception)
        {
            return string.Join("; ", exception.Errors.Select(error => $"{error.Code} {error.Message}"));
        }
    }
}
