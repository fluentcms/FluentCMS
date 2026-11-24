using System.Reflection;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.EndToEnd;

// The views of the accessibility sweep cover every route of the app (spec 4.18, 7): a page added later joins the list or
// fails here. The routes are read from the admin's assemblies that the app references, its lazy halves included; no browser
// is needed.
public sealed class AccessibilityViewsTests
{
    [Fact]
    public void EveryRouteOfTheAppIsAViewOfTheSweep()
    {
        var routes = AppRoutes();
        var covered = AccessibilityViews.All.SelectMany(view => view.Routes).ToHashSet(StringComparer.Ordinal);

        routes.Except(covered).Order(StringComparer.Ordinal).ShouldBeEmpty("routes of the app that no view of AccessibilityViews covers");
        covered.Except(routes).Order(StringComparer.Ordinal).ShouldBeEmpty("routes of AccessibilityViews that the app no longer has");
    }

    [Fact]
    public void EachViewHasANameOfItsOwn() =>
        AccessibilityViews.All.GroupBy(view => view.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ShouldBeEmpty();

    private static HashSet<string> AppRoutes()
    {
        var files = Directory.EnumerateFiles(AppContext.BaseDirectory, "FluentCMS.*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) is var name && (name.StartsWith("FluentCMS.Admin", StringComparison.Ordinal) || name.EndsWith(".Admin", StringComparison.Ordinal)))
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("FluentCMS.Admin.EndToEnd", StringComparison.Ordinal))
            .ToList();
        files.ShouldContain(path => Path.GetFileName(path) == "FluentCMS.Admin.App.dll");

        return [.. files.SelectMany(path => Types(Assembly.LoadFrom(path)))
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>(inherit: false))
            .Select(route => route.Template)];
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
