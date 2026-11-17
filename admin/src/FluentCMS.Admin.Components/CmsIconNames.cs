using System.Collections.Frozen;

namespace FluentCMS.Admin.Components;

/// <summary>
/// The icons a menu item or an action of an admin half may name: every icon of the wrapped set, by its name in camel case,
/// such as <c>comment</c> or <c>checkmark</c>. The names are written out here and not read from the enumeration, so the
/// lookup needs no reflection in a trimmed app and a name stays when a member is renamed.
/// </summary>
public static class CmsIconNames
{
    private static readonly FrozenDictionary<string, CmsIconName> Icons = new Dictionary<string, CmsIconName>(StringComparer.OrdinalIgnoreCase)
    {
        ["add"] = CmsIconName.Add,
        ["delete"] = CmsIconName.Delete,
        ["edit"] = CmsIconName.Edit,
        ["save"] = CmsIconName.Save,
        ["search"] = CmsIconName.Search,
        ["settings"] = CmsIconName.Settings,
        ["dismiss"] = CmsIconName.Dismiss,
        ["more"] = CmsIconName.More,
        ["upload"] = CmsIconName.Upload,
        ["folder"] = CmsIconName.Folder,
        ["document"] = CmsIconName.Document,
        ["image"] = CmsIconName.Image,
        ["person"] = CmsIconName.Person,
        ["globe"] = CmsIconName.Globe,
        ["info"] = CmsIconName.Info,
        ["warning"] = CmsIconName.Warning,
        ["checkmark"] = CmsIconName.Checkmark,
        ["open"] = CmsIconName.Open,
        ["sun"] = CmsIconName.Sun,
        ["moon"] = CmsIconName.Moon,
        ["navigation"] = CmsIconName.Navigation,
        ["up"] = CmsIconName.Up,
        ["down"] = CmsIconName.Down,
        ["comment"] = CmsIconName.Comment,
        ["plugin"] = CmsIconName.Plugin,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the icon of a name the set does not know.</summary>
    public static CmsIconName Default => CmsIconName.Plugin;

    /// <summary>Gets the names, in no order.</summary>
    public static IReadOnlyCollection<string> All => Icons.Keys;

    /// <summary>The icon of a name, whatever its case.</summary>
    /// <param name="name">The name, such as <c>comment</c>.</param>
    /// <returns>The icon, or <see cref="Default"/> for no name and for one the set does not know.</returns>
    public static CmsIconName Find(string? name) => TryFind(name, out var icon) ? icon : Default;

    /// <summary>The icon of a name, whatever its case.</summary>
    /// <param name="name">The name, such as <c>comment</c>.</param>
    /// <param name="icon">The icon, when the set knows the name.</param>
    /// <returns><see langword="true"/> when the set knows the name.</returns>
    public static bool TryFind(string? name, out CmsIconName icon)
    {
        icon = default;
        return name is not null && Icons.TryGetValue(name, out icon);
    }
}
