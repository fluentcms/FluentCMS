using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Plugins;

/// <summary>The section of the sidebar a menu item of an admin half is in.</summary>
[JsonConverter(typeof(AdminSectionJsonConverter))]
public enum AdminSection
{
    /// <summary>With the content types.</summary>
    Content,

    /// <summary>Media.</summary>
    Media,

    /// <summary>Administration, such as users and jobs.</summary>
    Administration,

    /// <summary>Settings.</summary>
    Settings,
}

/// <summary>The size a dashboard widget asks for on the home page.</summary>
[JsonConverter(typeof(WidgetSizeJsonConverter))]
public enum WidgetSize
{
    /// <summary>One column.</summary>
    Small,

    /// <summary>Two columns.</summary>
    Medium,

    /// <summary>The whole row.</summary>
    Wide,
}

/// <summary>
/// Declares a routable component as a page of an admin half. The component also carries the route for the router, as
/// <c>@page</c> writes it. Navigating to the route loads the half.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AdminPageAttribute : Attribute
{
    /// <summary>Declares a page.</summary>
    /// <param name="route">The route template under the app's base, <c>/{plugin}</c> or below it, such as <c>/reviews</c>.</param>
    public AdminPageAttribute([StringSyntax("Route")] string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        Route = route;
    }

    /// <summary>Gets the route template.</summary>
    public string Route { get; }

    /// <summary>Gets or sets the permission a user needs to see the page; none when <see langword="null"/>.</summary>
    public string? Permission { get; set; }

    /// <summary>
    /// Gets or sets whether the user needs <see cref="Permission"/> for the instance, as for a page whose calls answer 403
    /// to a user who holds it in a site only; in the site or for the instance when <see langword="false"/>.
    /// </summary>
    public bool InstanceOnly { get; set; }
}

/// <summary>Adds a menu item for a page of an admin half to the sidebar. The component also carries <see cref="AdminPageAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AdminMenuItemAttribute : Attribute
{
    /// <summary>Declares a menu item.</summary>
    /// <param name="label">The resource key of the label, in the half's resources, such as <c>Reviews.Menu</c>.</param>
    public AdminMenuItemAttribute(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
    }

    /// <summary>Gets the resource key of the label.</summary>
    public string Label { get; }

    /// <summary>Gets or sets the section of the sidebar.</summary>
    public AdminSection Section { get; set; }

    /// <summary>
    /// Gets or sets the name of the icon, one of the names of the wrapped component set, such as <c>comment</c>. A name
    /// the set does not know, or none, shows the icon of a plugin.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>Gets or sets the place among the items of the section; items of the same order are in the order of their labels.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the route the item links to, one of the component's pages. A component with one page needs none.
    /// </summary>
    public string? Route { get; set; }
}

/// <summary>Declares a component as the settings page of an area the plugin registers, in place of the generated page.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SettingsPageAttribute : Attribute
{
    /// <summary>Declares a settings page.</summary>
    /// <param name="area">The settings area, as <c>/settings/{area}</c> names it.</param>
    public SettingsPageAttribute(string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        Area = area;
    }

    /// <summary>Gets the settings area.</summary>
    public string Area { get; }
}

/// <summary>Declares a <see cref="Fields.FieldEditorBase"/> as the editor of a field type of the plugin.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FieldEditorAttribute : Attribute
{
    /// <summary>Declares a field editor.</summary>
    /// <param name="fieldType">The field type, as the schema names it.</param>
    public FieldEditorAttribute(string fieldType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        FieldType = fieldType;
    }

    /// <summary>Gets the field type.</summary>
    public string FieldType { get; }
}

/// <summary>Declares a <see cref="Lists.ListCellBase"/> as the list cell of a field type of the plugin.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ListCellAttribute : Attribute
{
    /// <summary>Declares a list cell.</summary>
    /// <param name="fieldType">The field type, as the schema names it.</param>
    public ListCellAttribute(string fieldType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        FieldType = fieldType;
    }

    /// <summary>Gets the field type.</summary>
    public string FieldType { get; }

    /// <summary>
    /// Gets or sets whether a list reads the field populated, for a field type the server populates as a reference, so that
    /// the cell shows what the field refers to without a read per row.
    /// </summary>
    public bool Populate { get; set; }
}

/// <summary>Declares an <see cref="Actions.EntryActionBase"/> as an action on one entry, in its form and its row.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EntryActionAttribute : Attribute
{
    /// <summary>Declares an entry action.</summary>
    /// <param name="name">The name of the action, unique in the plugin.</param>
    public EntryActionAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the name of the action.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the content types the action is offered on; every type when empty.</summary>
    public string[] ContentTypes { get; set; } = [];

    /// <summary>Gets or sets the permission a user needs to see the action; none when <see langword="null"/>.</summary>
    public string? Permission { get; set; }
}

/// <summary>Declares a <see cref="Actions.BulkActionBase"/> as an action on the selection of a list.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BulkActionAttribute : Attribute
{
    /// <summary>Declares a bulk action.</summary>
    /// <param name="name">The name of the action, unique in the plugin.</param>
    public BulkActionAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the name of the action.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the content types the action is offered on; every type when empty.</summary>
    public string[] ContentTypes { get; set; } = [];

    /// <summary>Gets or sets the permission a user needs to see the action; none when <see langword="null"/>.</summary>
    public string? Permission { get; set; }
}

/// <summary>Declares a <see cref="Actions.FieldActionBase"/> as an action on a field of a form.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FieldActionAttribute : Attribute
{
    /// <summary>Declares a field action.</summary>
    /// <param name="name">The name of the action, unique in the plugin.</param>
    public FieldActionAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the name of the action.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the field types the action is offered on; every type when empty.</summary>
    public string[] FieldTypes { get; set; } = [];

    /// <summary>Gets or sets the permission a user needs to see the action; none when <see langword="null"/>.</summary>
    public string? Permission { get; set; }
}

/// <summary>
/// Declares a component as a widget of the home page. It is shown to a user who holds <see cref="Permission"/> and has
/// <see cref="Role"/>, for each of the two that is set.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DashboardWidgetAttribute : Attribute
{
    /// <summary>Declares a dashboard widget.</summary>
    /// <param name="title">The resource key of the title, in the half's resources, such as <c>Reviews.Widget.Title</c>.</param>
    public DashboardWidgetAttribute(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    /// <summary>Gets the resource key of the title.</summary>
    public string Title { get; }

    /// <summary>Gets or sets the size the widget asks for.</summary>
    public WidgetSize Size { get; set; }

    /// <summary>Gets or sets the permission a user needs to see the widget; none when <see langword="null"/>.</summary>
    public string? Permission { get; set; }

    /// <summary>Gets or sets the name of the role a user needs to see the widget; none when <see langword="null"/>.</summary>
    public string? Role { get; set; }
}

/// <summary>
/// Declares a class as the half's implementation of a service other halves and the admin may ask for through
/// <see cref="IAdminPluginServices.FindAsync{TService}"/>, such as the media picker. The class is created by the provider
/// of the half, once per provider.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AdminServiceAttribute : Attribute
{
    /// <summary>Declares a service.</summary>
    /// <param name="serviceType">The service the class implements, an interface the asking half knows too.</param>
    public AdminServiceAttribute(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ServiceType = serviceType;
    }

    /// <summary>Gets the service the class implements.</summary>
    public Type ServiceType { get; }
}
