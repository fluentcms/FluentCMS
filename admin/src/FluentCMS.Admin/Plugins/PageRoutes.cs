using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Plugins;

// The route templates of a page that the route data the router renders it with agrees with, read from the route data alone
// and not from the address. The router does not set RouteData.Template; it gives each parameter of the page's templates a
// value, null for one the matched template has not, so a template agrees when it has a parameter for each value given and
// a value for each of its parameters that is not optional. Two templates with the same parameters both agree; a caller that
// gates by them requires what each requires.
internal static class PageRoutes
{
    public static IReadOnlyList<string> Of(RouteData routeData)
    {
        ArgumentNullException.ThrowIfNull(routeData);
        var given = routeData.RouteValues.Where(value => value.Value is not (null or "")).Select(value => value.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. routeData.PageType.GetCustomAttributes<RouteAttribute>(inherit: false).Select(route => route.Template)
            .Where(template => Parameters(template) is var parameters
                && given.All(parameters.ContainsKey)
                && parameters.All(parameter => parameter.Value || given.Contains(parameter.Key)))];
    }

    // The parameters of a template by name, true for one that may be absent: optional, or a catch-all.
    private static Dictionary<string, bool> Parameters(string template)
    {
        var parameters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in template.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is not ['{', .., '}'])
            {
                continue;
            }

            var name = segment[1..^1];
            var catchAll = name.StartsWith('*');
            name = name.TrimStart('*');
            var optional = catchAll || name.EndsWith('?');
            name = name.TrimEnd('?');
            var constraint = name.IndexOf(':', StringComparison.Ordinal);
            parameters[constraint < 0 ? name : name[..constraint]] = optional;
        }

        return parameters;
    }
}
