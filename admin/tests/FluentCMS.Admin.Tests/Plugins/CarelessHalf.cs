using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Tests.Plugins;

// The test assembly as a careless admin half: each declaration below is one the manifest check must name.
[assembly: FluentCmsAdminPlugin(typeof(CarelessPlugin))]

namespace FluentCMS.Admin.Tests.Plugins;

[FluentCmsAdminPlugin("careless")]
public sealed class CarelessPlugin;

[AdminPage("/careless")]
[AdminPage("/careless/other")]
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
[FieldAction("careless")]
[AdminService(typeof(IDisposable))]
public sealed class CarelessAction;

[AdminPage("/careless/item")]
[AdminMenuItem("Careless.Item", Route = "/careless/items")]
[AdminService(typeof(IDisposable))]
public sealed class CarelessTwin;
