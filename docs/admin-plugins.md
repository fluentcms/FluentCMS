# Writing an admin half

A plugin of FluentCMS has two halves: the server half, which the host loads, and the admin half, a Razor Class Library that the admin loads in the browser the first time one of its contributions is used. The admin half adds pages, menu items, a settings page, field editors, list cells, actions, dashboard widgets and services to the admin. It declares each with an attribute and lists the same in a manifest, `fluentcms-admin.json`, which the admin reads before it loads the half. The contract is the package `FluentCMS.Admin.Abstractions`.

Each example on this page is a file of the plugin template, under `templates/fluentcms-plugin/` in this repository, or of the sample half of the admin's tests, `admin/tests/FluentCMS.Admin.Plugins.Sample`. The templates job of CI builds and tests the template's output, and the admin's tests build and use the sample half, so both stay true to the contract. The [admin overview](admin.md) has the distributions, and [plugin migrations](plugin-migrations.md) the tables of the server half.

## Start from the template

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms-plugin -n Acme.Reviews
cd Acme.Reviews
dotnet test
```

The template writes the server half in `src/Acme.Reviews` and the admin half in `src/Acme.Reviews.Admin`, each with a test project under `tests/`; `--admin false` leaves the admin half out. The last segment of the name gives the classes their prefix and the plugin its name: the template's `Starter` files become `ReviewsAdminPlugin.cs`, `ReviewsPage.razor` and so on, the namespace `FluentCms_Plugin1.Admin` becomes `Acme.Reviews.Admin`, and the plugin is named `reviews`. This page names the template's files as they are in the repository.

| File of the template | Holds |
| --- | --- |
| `src/FluentCms-Plugin1.Admin/FluentCms-Plugin1.Admin.csproj` | the project and its three package references |
| `src/FluentCms-Plugin1.Admin/StarterAdminPlugin.cs` | the plugin class, its name, version and admin range, and `Configure` |
| `src/FluentCms-Plugin1.Admin/StarterPage.razor` | a page at `/starter-plugin` with its menu item |
| `src/FluentCms-Plugin1.Admin/StarterSettingsPage.razor` | the settings page of the plugin's settings area |
| `src/FluentCms-Plugin1.Admin/StarterWidget.razor` | a widget of the home page |
| `src/FluentCms-Plugin1.Admin/StarterApi.cs` | the half's client of the server half's endpoints, and its records |
| `src/FluentCms-Plugin1.Admin/StarterAdminResources.resx` | the half's texts |
| `src/FluentCms-Plugin1.Admin/wwwroot/fluentcms-admin.json` | the manifest |
| `tests/FluentCms-Plugin1.Admin.Tests/StarterAdminManifestTests.cs` | the manifest test |
| `tests/FluentCms-Plugin1.Admin.Tests/StarterPageTests.cs` | a bUnit test of the page, with the half's services as the admin builds them |

## The project

The half is a Razor Class Library for the browser (`<SupportedPlatform Include="browser" />`). Among the FluentCMS packages it references `FluentCMS.Admin.Abstractions`, the contract, and `FluentCMS.Admin.Components`, the wrapped component set, and no other. It adds `Microsoft.Extensions.Localization` for `IStringLocalizer<T>` over its own resources, which the two packages do not bring.

- **Never the server half.** The half runs in the browser and the server half on the host; the half reads the server half through the Management API, with records of its own, and repeats the plugin's name and permissions as constants (`StarterAdminPlugin.Name` and `NotesManage`).
- **Never `FluentCMS.Admin`.** The shell is the app's, and what a half may use of it is in the contract.
- **The wrapped set, not the library under it.** `FluentCMS.Admin.Components` keeps the compile assets of its component library private, so a half that names the library's types does not compile. Use the `Cms` components, such as `CmsCard`, `CmsButton` and `CmsNumberInput` in `StarterSettingsPage.razor`.
- **Records with source-generated JSON.** The admin is published trimmed, so a record the half reads has a `JsonSerializerContext`, as `StarterAdminJson` in `StarterApi.cs`.

The half's project does not set `IsTrimmable`: the app's partial trimming then keeps the half whole, the classes the manifest names among them. A half that sets it keeps its contribution classes itself.

## The plugin class

`StarterAdminPlugin.cs`:

```csharp
// The admin finds the half by this attribute when it loads the assembly.
[assembly: FluentCmsAdminPlugin(typeof(StarterAdminPlugin))]

namespace FluentCms_Plugin1.Admin;

[FluentCmsAdminPlugin(Name, Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class StarterAdminPlugin : IAdminPlugin
{
    public const string Name = "starter-plugin";

    public const string NotesManage = "starter-plugin.notes.manage";

    public void Configure(IAdminPluginBuilder admin) => admin.Services.AddScoped<StarterApi>();
}
```

- **The name** is the server plugin's name, as `GET /plugins` lists it, so the admin shows the half only in a site where that plugin is enabled. It is lower kebab case, and it may not be a first segment the admin keeps for its own routes: `content`, `settings`, `account`, `users`, `roles`, `api-keys`, `members`, `sites`, `plugins`, `jobs`, `system` or `authentication`.
- **The admin range** is the versions of `FluentCMS.Admin.Abstractions` the half was built for, in NuGet's range notation. The admin compares it with the assembly version of the `FluentCMS.Admin.Abstractions` it runs, so a pre-release label plays no part. A half whose range does not hold that version is not loaded: the admin leaves it out and lists it on `/system/plugins`, which the start page links to while there is one. A half whose manifest the admin cannot read, and a second half of the same name, are left out and listed the same way.
- **`Configure`** runs once, when the assembly is first loaded. The admin's container is built before then, so the services go into a provider of the half, which falls back to the admin's services, `IManagementApi` among them. A component of the half reaches that provider with `IAdminPluginServices.ProviderOf(GetType())`, as `StarterPage.razor` does; an `[Inject]` property of a service the half registers is refused, since the renderer creates the component from the admin's container. `admin.Filters` takes the filter operators of the half's field types ([list cells](#field-editors-and-list-cells)).

## The manifest and its test

`wwwroot/fluentcms-admin.json` is written by hand and ships as a static web asset of the half. It holds the plugin's name, the half's version and admin range, the name of its assembly, and one list per kind of contribution, each entry naming its class by its full name as `type`. The template's:

```json
{
  // Read by the admin before it loads the half, and kept in step with the attributes of the assembly by the manifest test.
  "manifestVersion": 1,
  "name": "starter-plugin",
  "version": "1.0.0",
  "adminRange": "[1.0.0,2.0.0)",
  "assembly": "FluentCms-Plugin1.Admin",
  "pages": [
    { "type": "FluentCms_Plugin1.Admin.StarterPage", "route": "/starter-plugin", "permission": "starter-plugin.notes.manage" }
  ],
  "menuItems": [
    {
      "type": "FluentCms_Plugin1.Admin.StarterPage",
      "label": "Starter.Menu",
      "labels": { "en": "Notes" },
      "route": "/starter-plugin",
      "section": "content",
      "icon": "document",
      "order": 100,
      "permission": "starter-plugin.notes.manage"
    }
  ],
  "settingsPages": [
    { "type": "FluentCms_Plugin1.Admin.StarterSettingsPage", "area": "starter-plugin" }
  ],
  "widgets": [
    { "type": "FluentCms_Plugin1.Admin.StarterWidget", "title": "Starter.Widget.Title", "size": "small", "permission": "starter-plugin.notes.manage" }
  ]
}
```

The file may hold comments and trailing commas. The lists are `pages`, `menuItems`, `settingsPages`, `fieldEditors`, `listCells`, `entryActions`, `bulkActions`, `fieldActions`, `widgets` and `services`; a list the half does not use is left out. Sections and sizes are written by name in lower case. The sample half's `wwwroot/fluentcms-admin.json` has an entry of every list.

The manifest test, `StarterAdminManifestTests.cs`, holds the file and the attributes together:

```csharp
[Fact]
public void TheManifestMatchesTheAttributesOfTheHalf() =>
    AdminPluginManifestCheck.VerifyFile(typeof(StarterAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
```

The test project copies the manifest to its output (`<None Include="../../src/FluentCms-Plugin1.Admin/wwwroot/fluentcms-admin.json" Link="fluentcms-admin.json" CopyToOutputDirectory="PreserveNewest" />`). `VerifyFile` fails with one sentence per difference: a contribution missing from the file or from the assembly, one whose route, field type, permission or other member differs, and a declaration the admin could not use, such as an `[AdminPage]` without its `@page`, a route outside `/{plugin}`, a reserved name, an admin range that is no range, an action that does not derive from its base class, a contribution declared twice, or a menu item without a text for a culture the half has resources in. Change the file in the same change as the attributes, and run the test.

## Contributions

| Kind | Attribute | Derives from | List of the manifest | Shown | Example |
| --- | --- | --- | --- | --- | --- |
| Page | `[AdminPage(route)]` with `@page` | `ComponentBase` | `pages` | at its route | `StarterPage.razor` |
| Menu item | `[AdminMenuItem(label)]` on its page | | `menuItems` | in the sidebar | `StarterPage.razor` |
| Settings page | `[SettingsPage(area)]` | `ComponentBase` | `settingsPages` | at `/settings/{area}`, in place of the generated page | `StarterSettingsPage.razor` |
| Field editor | `[FieldEditor(fieldType)]` | `FieldEditorBase` | `fieldEditors` | in the form, for a field of that type | `RatingEditor.razor` of the sample |
| List cell | `[ListCell(fieldType)]` | `ListCellBase` | `listCells` | in the list, for a column of that type | `RatingCell.razor` of the sample |
| Entry action | `[EntryAction(name)]` | `EntryActionBase` | `entryActions` | not yet: see below | `ApproveAction` in `ReviewActions.cs` of the sample |
| Bulk action | `[BulkAction(name)]` | `BulkActionBase` | `bulkActions` | in the bulk actions of a list of its content types | `ApproveAllAction` in `ReviewActions.cs` |
| Field action | `[FieldAction(name)]` | `FieldActionBase` | `fieldActions` | under a field of its field types in the form | `ShoutAction` in `ReviewActions.cs` |
| Dashboard widget | `[DashboardWidget(title)]` | `ComponentBase` | `widgets` | on the home page | `StarterWidget.razor` |
| Service for other halves | `[AdminService(serviceType)]` | the service's interface | `services` | to `IAdminPluginServices.FindAsync` | `SampleMediaPicker` in `ReviewActions.cs` |

The template has a page, a menu item, a settings page and a widget; the other kinds are in the sample half, whose components show what the declaration needs and little more. Every contribution shows only in a site where the server plugin is enabled, and only to a user who holds the permission it declares. The server checks every call again.

### Pages and menu items

`StarterPage.razor`:

```razor
@page "/starter-plugin"
@attribute [AdminPage("/starter-plugin", Permission = StarterAdminPlugin.NotesManage)]
@attribute [AdminMenuItem("Starter.Menu", Section = AdminSection.Content, Icon = "document", Order = 100)]
@inject IAdminPluginServices Plugins
@inject IStringLocalizer<StarterAdminResources> Text
```

- A page carries its route twice: `@page` for the router and `[AdminPage]` for the manifest. The route is `/{plugin}` or below it, and its last segment has no dot, since a host takes a last segment with a dot for a file. A page may have several routes, each with its permission, as `ReviewPage.razor` of the sample has `/reviews/{Id:guid}` and `/reviews/new`.
- The admin loads the half when the first segment of an address is the plugin's name, the plugin is enabled in the site, and the user holds the permission of the page whose route matches. Otherwise the address answers the not-found page, or a no-access state for a missing permission, and nothing is fetched.
- A menu item is declared on its page, links to it and takes its permission. `Section` is `Content`, `Media`, `Administration` or `Settings`, and `Order` its place among the section's items, then its label. `Icon` names an icon of the wrapped set in camel case, such as `document` or `comment`, which the manifest keeps; the sidebar draws no icon at present, as for the admin's own links.
- The label is a key of the half's resources, but the sidebar shows the item before the half is loaded, so the manifest carries its texts by culture name in `labels`: the sidebar shows the interface culture's, else its parent's, else `en`'s, which every item has.

### Settings pages

`StarterSettingsPage.razor` declares `[SettingsPage(StarterAdminPlugin.Name)]`: the settings area the server half registers, `starter-plugin`. The settings page of that area, at `/settings/starter-plugin`, then shows the half's page in place of the form the admin generates from the area's schema, while the plugin is enabled and the user holds `settings.starter-plugin.manage`. Only the area's own plugin replaces its page. The page reads and saves the area through the Management API with the version as `If-Match`, as `StarterApi.SaveSettingsAsync` does.

### Field editors and list cells

A field editor edits a field of a field type the server half registers, in the generated form. `RatingEditor.razor` of the sample:

```razor
@inherits FieldEditorBase
@attribute [FieldEditor("rating")]

<span>@Label: @Value</span>
```

`FieldEditorBase` of `FluentCMS.Admin.Fields` gives the editor the field's description (`Field`), its value as JSON (`Value`), its errors, `ReadOnly`, `Required`, the content locale and its text direction (`ContentLocale`, `TextDirection`), the values of the other fields (`Document`) and `OnBlur`. The editor raises a change with `ChangeAsync`. It may override `FocusAsync`, which the form calls for the first field with an error, and `FlushAsync`, which the form calls before a save, for an editor that holds a change back. The manifest names it in `fieldEditors`, with `fieldType`. A field type without an editor shows its JSON read-only.

A `[FieldEditor]` for a core field type, such as `text` or `date`, replaces the admin's own editor of that type in every form, not only beside it. While the plugin is disabled in a site, that type then has no editor there, and its fields show their JSON read-only, even though the core editor exists. A `[ListCell]` for a core field type replaces the core cell in the same way. Declare editors and cells for the plugin's own field types unless the half means to take a core type over in every site where the plugin is enabled.

A list cell shows the value of such a field in a list. `RatingCell.razor` of the sample derives from `ListCellBase` of `FluentCMS.Admin.Lists`, which gives it `Field` and `Entry`, and `Value`; `[ListCell(fieldType, Populate = true)]` asks the list to read the field populated, for a type the server populates as a reference. The filter operators of the type are registered in `Configure`, as `ReviewsAdminPlugin.cs` of the sample does:

```csharp
admin.Filters.Add("rating", FilterOperand.WholeNumber, FilterOperator.Equal, FilterOperator.GreaterThan);
```

The form and the list load the half when they open with a field of its type. The template's `_Imports.razor` has neither namespace; add `@using FluentCMS.Admin.Fields` and `@using FluentCMS.Admin.Lists` to it, as the sample's has.

### Actions

The three kinds of action are plain classes of `FluentCMS.Admin.Actions`, which the half's provider creates, so their constructors take the half's services, as `ApproveAction(ReviewsApi api)` does. Each has a `Label`, an optional `Icon` and a `ConfirmationText`; the targets and the permission are on the attribute, since the admin needs them before the half loads. `ContentTypes` or `FieldTypes` left empty means every type.

- **A bulk action** (`ApproveAllAction`) is offered among the bulk actions of a list of its content types, and runs on the selected entries with a `BulkActionContext`, reporting each entry to the `IBulkProgress` it is given.
- **A field action** (`ShoutAction`) is offered under a field of its field types, through a button "Actions" under the editor, while the user may change the field. `RunAsync` answers a `FieldActionResult`: `Unchanged`, or `Replace(value)`, which goes into the draft as an unsaved change. `FieldsFrom` names a read of the Management API whose answer says which fields of those types the action is offered on, such as `ai/fields#fields`, so that the form needs no load of the half to know.
- **An entry action** (`ApproveAction`) is declared, created and checked like the others, but no view of the admin shows one yet.

### Dashboard widgets

`StarterWidget.razor`:

```razor
@attribute [DashboardWidget("Starter.Widget.Title", Size = WidgetSize.Small, Permission = StarterAdminPlugin.NotesManage)]
```

The home page shows the widgets of the enabled halves after its own tiles, by the plugin's name and then in the order of the manifest. `Size` is `Small`, `Medium` or `Wide`: one column, two, or the whole row. A widget shows to a user who holds its `Permission` and has its `Role`, for each that is set, as `LatestReviewsWidget.razor` of the sample sets both. Only a widget that shows loads its half. The widget draws its own title, a `CmsCard` with its `Title` from the half's resources, and keeps a failure in its tile, as the template's does.

### Services for other halves

`SampleMediaPicker` in `ReviewActions.cs` of the sample:

```csharp
[AdminService(typeof(IMediaPicker))]
public sealed class SampleMediaPicker : IMediaPicker
```

The manifest names it in `services`, with `type` and the full name of the interface as `service`. Another half asks for the service with `IAdminPluginServices.FindAsync<IMediaPicker>()`, which loads the enabled half that provides it and answers its instance, or null when no enabled half does. The interface is in `FluentCMS.Admin.Abstractions`, so the half that asks names it without a reference to the half that provides it. The media half provides `IMediaPicker` this way.

## What the admin gives a component

A component of a half injects these services of the admin, all in `FluentCMS.Admin.Abstractions`:

| Service | Gives |
| --- | --- |
| `IManagementApi` | the admin's client of the Management API, already set to the site and the content locale, with the user's token and the wait on a 429. An `ApiRequest` carries its `IfMatch` and `IdempotencyKey`, an answer its `EntityTag`, and a refused call throws `ApiProblemException` with the `ApiProblem` |
| `IManagementEvents` | a request whose answer is `text/event-stream`, read event by event through the same client |
| `IAdminContext` | the user, the site, the content locale and the user's access, with a `Changed` event and `ReadSitesAsync` |
| `IAdminAccess` | `Allows(permission)` in the site, `AllowsForInstance(permission)` for the instance, and `ForType(type)` for a content type |
| `IAdminFormats` | the user's time zone, and a date, a time, a moment and a number formatted in the user's formats |
| `IAdminSchema` | the schema of the site |
| `IAnnouncer` | a message for the shell's polite or assertive live region |
| `IAdminOverlays` | a dialog or another overlay shown over the page |
| `IBulkProgressService` | the progress of a long task of a page |
| `IRelationPicker` | the admin's picker of entries |
| `IFormDraftStore` | the unsaved forms, kept across the end of a session |
| `IAdminPluginServices` | the provider of a loaded half, and the services of other halves |
| `AdminConfiguration` | the admin's `config.json` ([admin configuration](admin-configuration.md#configjson)) |

`IStringLocalizer<T>` reads the half's own resources, and the wrapped components of `FluentCMS.Admin.Components` draw the page. A page that shows a moment formats it with `IAdminFormats`, in the user's time zone, as `ReviewPage.razor` of the sample does. The admin gives no service of its own to a half that the contract does not name.

## Access for the site and for the instance

A permission is held in a site by default: `Permission` of a page, a menu item, an action or a widget is checked against the user's access in the current site, which `IAdminAccess.Allows` answers. A permission the server checks for the instance only, such as one of a page that manages something every site shares, is declared with `InstanceOnly = true` on `[AdminPage]`; the manifest carries `instanceOnly` on the page and on its menu item, and the sidebar and the route ask `IAdminAccess.AllowsForInstance`. An instance-only page needs a permission, and the manifest test names one that has none. A page without a permission that is not instance-only shows to every signed-in user of a site where the plugin is enabled.

The admin hides what the user may not use; the server half still checks every call, since a hidden button is no guard.

## Use the half in an admin app

The prebuilt admin holds the first-party halves only, so a plugin's own half runs in an admin app of your own, made with `dotnet new fluentcms-admin`. Reference the half's project or its package from the app, and list its assembly as lazy:

```xml
<ItemGroup>
  <ProjectReference Include="../Acme.Reviews/src/Acme.Reviews.Admin/Acme.Reviews.Admin.csproj" />
  <BlazorWebAssemblyLazyLoad Include="Acme.Reviews.Admin.wasm" />
</ItemGroup>
```

The app's build writes the `fluentcms-admin.json` of every half it references into `fluentcms-admin.plugins.json`, which the admin reads at start with one request, and stops when a half is not listed as lazy, naming the line to add. The first download then holds no part of the half; the browser fetches it the first time one of its contributions is used, once per page load. The [template's README](../templates/fluentcms-admin/README.md#add-an-admin-half-of-your-own) has the rest, and how to serve the app.

A half that fails to load gives an error state with a retry. A component that the admin cannot create, such as one with an `[Inject]` property of a service the admin does not have, shows an error in its place, and the rest of the admin keeps working.

## Versions

The half's package has a version of its own, the `Version` of `[FluentCmsAdminPlugin]` and `version` of the manifest. `AdminRange` says which versions of the contract it runs on: `FluentCMS.Admin.Abstractions` and `FluentCMS.Admin.Components` follow semantic versioning from 1.0.0 on, so a range of one major version, such as `[1.0.0,2.0.0)`, holds every admin of that major version. A preview of the packages may break the contract between previews, and every preview of 1.0.0 has the assembly version 1.0.0, which the range is compared with, so an admin loads a half built against another preview: build the half again for each preview. The other admin packages are a preview without that promise ([releasing](releasing.md#the-admin-packages)).

## The half's records and the OpenAPI document

The half's records are its own, such as `StarterNote` and `StarterSettingsValue` in `StarterApi.cs`, read from the server half's endpoints through `IManagementApi`. Nothing holds them to the server half's answers but the half's own tests, since the half never references the server half. The first-party halves are held to the reference host's OpenAPI document by the API tests of this repository, `admin/tests/FluentCMS.Admin.ApiTests`: `OpenApiContractTests` checks that every member a record reads is in the schema of the answer it reads and that every member it requires the schema requires too, and each half's tests check its records the same way, as `TheRecordsOfTheHalfMatchTheOpenApiDocument` in `WebhooksTests.cs`. The template has no such test; a team's host serves the same document at `/openapi/v1.json` in Development ([OpenAPI](host-configuration.md#openapi-and-the-scalar-ui)), with the plugin's Management routes in it.

## Resources and menu texts

The half's texts are in `.resx` files beside its code, such as `StarterAdminResources.resx` with its marker class `StarterAdminResources`, read through `IStringLocalizer<StarterAdminResources>`. The keys of the menu item's label and the widget's title are keys of these resources too.

A translation of the half adds a file per culture beside the neutral one, such as `ReviewsResources.fr.resx` of the sample, which the build makes a satellite assembly. The menu item then needs a text of that culture, or of a parent of it other than `en`, in its `labels`, since the sidebar shows it before the half loads: the sample's manifest has `"labels": { "en": "Reviews", "fr": "Avis" }`, and its manifest test fails without `fr`. [Translating the admin](admin-translating.md) has the rules of the files.

## Field editors and accessibility

The admin's target is WCAG 2.2 AA, and a field editor of a half sits in the admin's forms among the admin's own. An editor that users see meets the rules of the admin's own editors, such as `admin/src/FluentCMS.Admin/Fields/TextFieldEditor.razor`:

- **Draw a wrapped input** of `FluentCMS.Admin.Components`, such as `CmsTextInput`, `CmsNumberInput` or `CmsSelect`, and pass it `Label`, `Help`, `Error` (from `ErrorText`), `Required` and `ReadOnly`. The input then has a visible label, `aria-describedby` for its help and its error, `aria-required` and `aria-invalid`, and a read-only input stays in the tab order.
- **Set the language and direction of the value**, `Lang` from `ContentLocale` and `Dir` from `TextDirection`, so that the value is in the content locale while the label stays in the interface's language and direction. A value that is not text in a language, such as an email address, stays left to right.
- **Override `FocusAsync`** to move focus to the input, so that the form can take the user to the first field with an error.
- **Raise `OnBlur`** when focus leaves the input, so that the form can check the field as the user leaves it.
- **Work by keyboard alone**, with a visible focus and targets of at least 24 by 24 CSS pixels, and give a dragged interaction another way.
- **Put every text in the resources**, so that it is translated with the half and marked under the pseudo-cultures ([translating the admin](admin-translating.md#the-pseudo-cultures)).

[Accessibility of the admin](admin-accessibility.md) says what CI checks of every page, a half's among them, and holds the script of the screen reader pass, whose steps a half's page can follow.

## What a half cannot do

- **Add services to the admin's container.** The container is built before the half loads; the half's services are in its own provider, reached through `IAdminPluginServices`.
- **Add script to the page.** The admin offers no slot for script, and the page's `Content-Security-Policy` allows scripts of the admin's own origin only. A component may use JavaScript interop with a module of its own package.
- **Call another origin.** The page's `connect-src` allows the admin's own origin, the API and the authority. The half reaches the server through the Management API, which is where the server half's endpoints are.
- **Extend the rich text editor**, or replace the admin's own pages. A settings page replaces only the generated page of its own plugin's area.
- **Use routes outside `/{plugin}`**, or a last segment with a dot.
- **Show anything in a site where its plugin is disabled**, or to a user without the permission it declares. The half is not even loaded there.
