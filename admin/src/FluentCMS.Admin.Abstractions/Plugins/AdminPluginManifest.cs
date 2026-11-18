using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FluentCMS.Admin.Plugins;

/// <summary>
/// The manifest of an admin half, <c>fluentcms-admin.json</c>: what the shell knows of the half before its assembly is
/// loaded. It is a static web asset of the half's package and says what the attributes of the assembly say, which
/// <see cref="AdminPluginManifestCheck"/> holds a test to. Each contribution names its class by its full name.
/// </summary>
public sealed record AdminPluginManifest
{
    // The source-generated metadata sets every init-only member of a type with required members, an absent one to its
    // default, so the members below answer for an absent value themselves and not with an initializer.

    /// <summary>The name of the manifest file among the static web assets of a half.</summary>
    public const string FileName = "fluentcms-admin.json";

    /// <summary>The version of the manifest's format that this version of the contract writes.</summary>
    public const int CurrentManifestVersion = 1;

    /// <summary>The version of the manifest's format. A later version only adds members, which an earlier reader skips.</summary>
    public int ManifestVersion { get => field == 0 ? CurrentManifestVersion : field; init; }

    /// <summary>The name of the server plugin, as <c>GET /plugins</c> lists it.</summary>
    public required string Name { get; init; }

    /// <summary>The version of the admin half.</summary>
    public string? Version { get; init; }

    /// <summary>The range of <c>FluentCMS.Admin.Abstractions</c> the half was built for, in NuGet's notation.</summary>
    public string? AdminRange { get; init; }

    /// <summary>The name of the half's assembly, without an extension.</summary>
    public required string Assembly { get; init; }

    /// <summary>The pages.</summary>
    public IReadOnlyList<AdminManifestPage> Pages { get => field ?? []; init; }

    /// <summary>The menu items.</summary>
    public IReadOnlyList<AdminManifestMenuItem> MenuItems { get => field ?? []; init; }

    /// <summary>The settings pages.</summary>
    public IReadOnlyList<AdminManifestSettingsPage> SettingsPages { get => field ?? []; init; }

    /// <summary>The field editors.</summary>
    public IReadOnlyList<AdminManifestFieldComponent> FieldEditors { get => field ?? []; init; }

    /// <summary>The list cells.</summary>
    public IReadOnlyList<AdminManifestFieldComponent> ListCells { get => field ?? []; init; }

    /// <summary>The entry actions.</summary>
    public IReadOnlyList<AdminManifestAction> EntryActions { get => field ?? []; init; }

    /// <summary>The bulk actions.</summary>
    public IReadOnlyList<AdminManifestAction> BulkActions { get => field ?? []; init; }

    /// <summary>The field actions.</summary>
    public IReadOnlyList<AdminManifestFieldAction> FieldActions { get => field ?? []; init; }

    /// <summary>The dashboard widgets.</summary>
    public IReadOnlyList<AdminManifestWidget> Widgets { get => field ?? []; init; }

    /// <summary>The services the half provides to other halves and the admin.</summary>
    public IReadOnlyList<AdminManifestService> Services { get => field ?? []; init; }

    /// <summary>
    /// Whether every contribution is one this version of the contract reads: no list holds a null, and every section and
    /// size is known. A reader of a list of manifests leaves out and reports the manifest of a half that is not, such as
    /// one written for a later version, and still has the others.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        Pages.All(page => page is not null)
        && MenuItems.All(item => item is not null && ManifestNames.IsKnown(item.Section) && item.Labels.Values.All(label => label is not null))
        && SettingsPages.All(page => page is not null)
        && FieldEditors.All(editor => editor is not null)
        && ListCells.All(cell => cell is not null)
        && EntryActions.All(action => action is not null)
        && BulkActions.All(action => action is not null)
        && FieldActions.All(action => action is not null)
        && Widgets.All(widget => widget is not null && ManifestNames.IsKnown(widget.Size))
        && Services.All(service => service is not null);
}

/// <summary>A page of an admin half, from <see cref="AdminPageAttribute"/>.</summary>
public sealed record AdminManifestPage
{
    /// <summary>The full name of the component.</summary>
    public required string Type { get; init; }

    /// <summary>The route template.</summary>
    public required string Route { get; init; }

    /// <summary>The permission a user needs to see the page, if it needs one.</summary>
    public string? Permission { get; init; }
}

/// <summary>A menu item of an admin half, from <see cref="AdminMenuItemAttribute"/>.</summary>
public sealed record AdminManifestMenuItem
{
    /// <summary>The full name of the page's component.</summary>
    public required string Type { get; init; }

    /// <summary>The resource key of the label.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The texts of the label by culture name, such as <c>en</c> and <c>fr</c>, which the sidebar shows before the half is
    /// loaded: the text of the interface culture, else of its parent cultures, else of <see cref="NeutralCulture"/>, which
    /// every item has.
    /// </summary>
    public IReadOnlyDictionary<string, string> Labels { get => field ?? EmptyLabels; init; }

    /// <summary>The culture of the label text every menu item has, the admin's neutral culture.</summary>
    public const string NeutralCulture = "en";

    private static readonly IReadOnlyDictionary<string, string> EmptyLabels = new Dictionary<string, string>();

    /// <summary>The text of the label for a culture: of the culture, else of its parent cultures, else of <see cref="NeutralCulture"/>.</summary>
    /// <param name="culture">The interface culture.</param>
    /// <returns>The text, or <see cref="Label"/> when the item has no text for any of them.</returns>
    public string LabelFor(System.Globalization.CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            if (Labels.TryGetValue(current.Name, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return Labels.TryGetValue(NeutralCulture, out var neutral) && !string.IsNullOrWhiteSpace(neutral) ? neutral : Label;
    }

    /// <summary>The route the item links to.</summary>
    public required string Route { get; init; }

    /// <summary>The section of the sidebar.</summary>
    public AdminSection Section { get; init; }

    /// <summary>The name of the icon, if the item names one.</summary>
    public string? Icon { get; init; }

    /// <summary>The place among the items of the section.</summary>
    public int Order { get; init; }

    /// <summary>The permission of the page the item links to, if it needs one.</summary>
    public string? Permission { get; init; }
}

/// <summary>A settings page of an admin half, from <see cref="SettingsPageAttribute"/>.</summary>
public sealed record AdminManifestSettingsPage
{
    /// <summary>The full name of the component.</summary>
    public required string Type { get; init; }

    /// <summary>The settings area.</summary>
    public required string Area { get; init; }
}

/// <summary>A field editor or a list cell of an admin half, from <see cref="FieldEditorAttribute"/> or <see cref="ListCellAttribute"/>.</summary>
public sealed record AdminManifestFieldComponent
{
    /// <summary>The full name of the component.</summary>
    public required string Type { get; init; }

    /// <summary>The field type.</summary>
    public required string FieldType { get; init; }
}

/// <summary>An entry action or a bulk action of an admin half, from <see cref="EntryActionAttribute"/> or <see cref="BulkActionAttribute"/>.</summary>
public sealed record AdminManifestAction
{
    /// <summary>The full name of the action's class.</summary>
    public required string Type { get; init; }

    /// <summary>The name of the action.</summary>
    public required string Name { get; init; }

    /// <summary>The content types the action is offered on; every type when empty.</summary>
    public IReadOnlyList<string> ContentTypes { get => field ?? []; init; }

    /// <summary>The permission a user needs to see the action, if it needs one.</summary>
    public string? Permission { get; init; }
}

/// <summary>A field action of an admin half, from <see cref="FieldActionAttribute"/>.</summary>
public sealed record AdminManifestFieldAction
{
    /// <summary>The full name of the action's class.</summary>
    public required string Type { get; init; }

    /// <summary>The name of the action.</summary>
    public required string Name { get; init; }

    /// <summary>The field types the action is offered on; every type when empty.</summary>
    public IReadOnlyList<string> FieldTypes { get => field ?? []; init; }

    /// <summary>The permission a user needs to see the action, if it needs one.</summary>
    public string? Permission { get; init; }
}

/// <summary>A dashboard widget of an admin half, from <see cref="DashboardWidgetAttribute"/>.</summary>
public sealed record AdminManifestWidget
{
    /// <summary>The full name of the component.</summary>
    public required string Type { get; init; }

    /// <summary>The resource key of the title.</summary>
    public required string Title { get; init; }

    /// <summary>The size the widget asks for.</summary>
    public WidgetSize Size { get; init; }

    /// <summary>The permission a user needs to see the widget, if it needs one.</summary>
    public string? Permission { get; init; }

    /// <summary>The role a user needs to see the widget, if it needs one.</summary>
    public string? Role { get; init; }
}

/// <summary>A service of an admin half, from <see cref="AdminServiceAttribute"/>.</summary>
public sealed record AdminManifestService
{
    /// <summary>The full name of the class that implements the service.</summary>
    public required string Type { get; init; }

    /// <summary>The full name of the service.</summary>
    public required string Service { get; init; }
}

/// <summary>
/// Reads and writes manifests as JSON with source-generated metadata, so a trimmed app reads them. Member names are in
/// camel case, sections and sizes are written by name in lower case, and a hand-written file may hold comments and trailing
/// commas. A member, a section or a size that this version does not know does not stop the read: the member is skipped,
/// and the section or the size reads as a value outside its enumeration, which <see cref="AdminPluginManifest.IsValid"/>
/// tells. A null where the manifest takes none is refused, but for a null in a list, which <c>IsValid</c> tells too.
/// </summary>
public static class AdminPluginManifestJson
{
    /// <summary>Gets the metadata of one manifest, the file of a half.</summary>
    public static JsonTypeInfo<AdminPluginManifest> Manifest => AdminPluginManifestJsonContext.Default.AdminPluginManifest;

    /// <summary>Gets the metadata of a list of manifests, the file an app's build gathers from the halves it references.</summary>
    public static JsonTypeInfo<IReadOnlyList<AdminPluginManifest>> Manifests => AdminPluginManifestJsonContext.Default.IReadOnlyListAdminPluginManifest;

    /// <summary>Reads a manifest.</summary>
    /// <param name="json">The text of <c>fluentcms-admin.json</c>.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="JsonException">The text is not a manifest.</exception>
    public static AdminPluginManifest Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, Manifest) ?? throw new JsonException("The manifest is null.");
    }

    /// <summary>Writes a manifest.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The text of <c>fluentcms-admin.json</c>.</returns>
    public static string Write(AdminPluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, Manifest);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    RespectNullableAnnotations = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AdminPluginManifest))]
[JsonSerializable(typeof(IReadOnlyList<AdminPluginManifest>))]
internal sealed partial class AdminPluginManifestJsonContext : JsonSerializerContext
{
}
