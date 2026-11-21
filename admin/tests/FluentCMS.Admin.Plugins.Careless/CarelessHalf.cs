using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Careless;

[assembly: FluentCmsAdminPlugin(typeof(CarelessPlugin))]

namespace FluentCMS.Admin.Plugins.Careless;

// A route of the router only, outside the plugin's segment.
[Microsoft.AspNetCore.Components.Route("/content/careless")]
public sealed class CarelessRoute;

[FluentCmsAdminPlugin("system", AdminRange = "soon")]
public sealed class CarelessPlugin;

[AdminPage("/system/careless")]
[AdminPage("/system/careless/other", InstanceOnly = true)]
[AdminMenuItem("Careless.Pages")]
[SettingsPage("careless")]
[DashboardWidget("Careless.Widget")]
public sealed class CarelessPage;

[AdminMenuItem("Careless.Menu")]
[FieldEditor("careless")]
[ListCell("careless")]
public sealed class CarelessMenuItem;

[EntryAction("careless")]
[BulkAction("careless")]
[FieldAction("careless", FieldsFrom = "/ai/fields")]
[AdminService(typeof(IDisposable))]
public sealed class CarelessAction;

[AdminPage("/careless/item")]
[AdminMenuItem("Careless.Item", Route = "/careless/items")]
[AdminService(typeof(IDisposable))]
public sealed class CarelessTwin;
