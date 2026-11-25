global using FluentCMS.Admin.PseudoLocalization;
using System.Globalization;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.PseudoLocalization;

// The checks of the pseudo-cultures in the component tests of every admin assembly (spec 7), beside the marking that
// admin/eng/PseudoCulture.cs shares with the build: a visible text that is not marked did not come from a resource.
internal static partial class PseudoCulture
{
    private static readonly string[] TextAttributes =
    [
        "aria-label", "aria-description", "aria-valuetext", "aria-roledescription", "title", "placeholder", "alt", "label",
    ];

    private static readonly string[] ButtonInputTypes = ["button", "submit", "reset"];

    // A text is localized when no letter is left once its marked segments are taken out, so that a hard-coded word between
    // two resources fails, and numbers and punctuation pass.
    public static bool IsLocalized(string text)
    {
        var depth = 0;
        foreach (var c in text)
        {
            if (c == Open)
            {
                depth++;
            }
            else if (c == Close && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    // The visible texts and the text attributes of the markup that are not localized, data aside.
    public static IReadOnlyList<string> Unlocalized(INodeList nodes)
    {
        var found = new List<string>();
        foreach (var node in nodes)
        {
            Collect(node, found);
        }

        return found;
    }

    public static int TextCount(INodeList nodes) =>
        nodes.Sum(node => node.GetDescendantsAndSelf().OfType<IText>().Count(text => text.Data.Trim().Length > 0));

    private static void Collect(INode node, List<string> found)
    {
        // A style or script element holds code, not text; the library's drawer renders a style element.
        if (node is IElement { LocalName: "style" or "script" })
        {
            return;
        }

        if (node is IText text)
        {
            var value = text.Data.Trim();
            if (!IsLocalized(value))
            {
                found.Add(value);
            }
        }

        if (node is IElement element)
        {
            var attributes = element.LocalName == "input" && ButtonInputTypes.Contains(element.GetAttribute("type"))
                ? TextAttributes.Append("value")
                : TextAttributes;
            foreach (var name in attributes)
            {
                var value = element.GetAttribute(name)?.Trim();
                if (value is not null && !IsLocalized(value))
                {
                    found.Add($"{name}=\"{value}\"");
                }
            }
        }

        foreach (var child in node.ChildNodes)
        {
            Collect(child, found);
        }
    }
}

internal sealed class PseudoStringLocalizerFactory(IStringLocalizerFactory inner, CultureInfo culture) : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => new PseudoStringLocalizer(inner.Create(resourceSource), culture);

    public IStringLocalizer Create(string baseName, string location) => new PseudoStringLocalizer(inner.Create(baseName, location), culture);
}

// A key missing from the resources comes out unmarked, so the check also fails on it.
internal sealed class PseudoStringLocalizer(IStringLocalizer inner, CultureInfo culture) : IStringLocalizer
{
    public LocalizedString this[string name] => Mark(inner[name]);

    public LocalizedString this[string name, params object[] arguments] => Mark(inner[name, arguments]);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => inner.GetAllStrings(includeParentCultures).Select(Mark);

    private LocalizedString Mark(LocalizedString value) =>
        value.ResourceNotFound
            ? value
            : new LocalizedString(value.Name, PseudoCulture.Transform(value.Value, culture.Name), false, value.SearchedLocation);
}

// The wrapped set's texts through the same localizer, as the admin reads them (CmsTextLocalizer), so that they are marked.
internal sealed class PseudoCmsTexts(IStringLocalizerFactory factory) : ICmsTextSource
{
    private readonly IStringLocalizer localizer = factory.Create("Resources.CmsStrings", typeof(ICmsTextSource).Assembly.GetName().Name!);

    public string? GetText(string name) => localizer[name] is { ResourceNotFound: false } text ? text.Value : null;
}

internal static class PseudoCultureContext
{
    // The resources of every assembly marked in the culture, and the culture the current one: call after the context's own
    // registrations, before the first render.
    public static void UsePseudoCulture(this BunitContext context, string culture)
    {
        ArgumentNullException.ThrowIfNull(context);
        var info = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = info;
        var resources = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        var factory = new PseudoStringLocalizerFactory(resources, info);
        context.Services.Replace(ServiceDescriptor.Singleton<IStringLocalizerFactory>(factory));
        context.Services.Replace(ServiceDescriptor.Singleton<ICmsTextSource>(new PseudoCmsTexts(factory)));
    }
}

// The sweep of spec 7 in the test project of each assembly: every routable page under both pseudo-cultures, with one heading
// and no text that is not from the resources. Data a fake serves is marked as a resource is, with PseudoCulture.Transform.
internal static class PseudoSweep
{
    public static TheoryData<string, string> Pages(Assembly assembly)
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in PseudoCulture.Names)
        {
            foreach (var page in RoutablePages(assembly))
            {
                data.Add(culture, page.Name);
            }
        }

        return data;
    }

    public static IEnumerable<Type> RoutablePages(Assembly assembly) =>
        assembly.GetTypes().Where(type => type.IsDefined(typeof(Microsoft.AspNetCore.Components.RouteAttribute))).OrderBy(type => type.Name, StringComparer.Ordinal);

    // A page has one heading; a part of a page, such as a field editor, none of its own.
    public static void ShouldBeLocalized<T>(this IRenderedComponent<T> cut, bool page = true)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        ArgumentNullException.ThrowIfNull(cut);
        cut.FindAll("h1").Count.ShouldBe(page ? 1 : 0);
        PseudoCulture.TextCount(cut.Nodes).ShouldBeGreaterThan(1);
        PseudoCulture.Unlocalized(cut.Nodes).ShouldBeEmpty();
    }

    public static Exception NoSweep(string page) =>
        new InvalidOperationException($"The page {page} has no sweep: render it in this test with what it reads.");
}
