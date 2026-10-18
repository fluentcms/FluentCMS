using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using FluentCMS.Admin.Components.Pages;
using FluentCMS.Admin.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FluentCMS.Tests.Admin;

// admin.js is the admin's one interop module (ADR-0011): its listeners are delegated and read the DOM when an event arrives,
// so no component calls into JavaScript and nothing breaks when the circuit is lost or rejoined.
public sealed class InteropModuleTests
{
    private static readonly string Admin = Path.Combine(ProjectReferenceRulesTests.FindRepositoryRoot(), "src", "FluentCMS.Admin");

    [Fact]
    public void Admin_js_is_the_only_script_the_admin_ships()
    {
        var scripts = Directory.EnumerateFiles(Path.Combine(Admin, "wwwroot"), "*.*js", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Admin, path).Replace('\\', '/'));

        Assert.Equal(["wwwroot/js/admin.js"], scripts);
    }

    [Fact]
    public void No_component_calls_into_javascript()
    {
        var injectsRuntime = typeof(Button).Assembly.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .Where(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(property => property.GetCustomAttribute<InjectAttribute>() is not null && typeof(IJSRuntime).IsAssignableFrom(property.PropertyType)))
            .Select(type => type.FullName);

        Assert.Empty(injectsRuntime);
    }

    [Fact]
    public void Only_the_root_component_loads_scripts()
    {
        var withScripts = Directory.EnumerateFiles(Path.Combine(Admin, "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), "<script", RegexOptions.IgnoreCase))
            .Select(Path.GetFileName);

        Assert.Equal(["App.razor"], withScripts);
    }

    [Fact]
    public void Shortcuts_on_the_showcase_are_declared_with_aria_keyshortcuts()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();

        Assert.Equal("Control+K Meta+K", cut.Find("[data-ui=command-input]").GetAttribute("aria-keyshortcuts"));
        Assert.Equal("Alt+T", cut.Find("[data-slot=toaster]").GetAttribute("aria-keyshortcuts"));
    }
}
