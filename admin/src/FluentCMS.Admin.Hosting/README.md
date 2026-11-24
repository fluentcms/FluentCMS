# FluentCMS.Admin.Hosting

Serves the [FluentCMS](https://github.com/fluentcms/FluentCMS) admin from an ASP.NET Core host at `/admin`: the app's files, its `index.html` with the base set, its `config.json` from the host's configuration, the registration of the admin client and the headers of the admin page.

```csharp
builder.Services.AddFluentCms(cms =>
{
    cms.UseSqlite(builder.Configuration.GetConnectionString("cms"));
    cms.AddAdmin();
});

var app = builder.Build();
app.UseFluentCms();
app.MapFluentCms();
app.MapFluentCmsAdmin();
```

Turn the admin on with `FluentCms:Admin:Enabled`. Which app the host serves depends on its project file:

- **The package's first-party build.** A host that references this package and no admin app's project serves the build the package carries: `FluentCMS.Admin.App` with the first-party admin halves, the same files as `fluentcms-admin-{version}.zip` of the release. They become the host's static web assets, so its build serves them, as `dotnet run` does, and its publish copies them into `wwwroot` with their compressed copies.
- **An app project of the host's own.** A host that also references an admin app's project, such as one made with `dotnet new fluentcms-admin` to hold a team's own admin halves, serves that app, and the package's build stays out of the host. The package tells an admin app by `fluentcms-admin.plugins.json`, the manifest the app's build gathers from its halves, so the reference itself is the switch: remove it and the host serves the package's build again. A WebAssembly project that is no admin app, such as the `.Client` project of a Blazor Web App, is not taken for one.

In a host that references an admin app's project, the component library's icon assemblies, which the app brings and which run in the browser only, stay out of the host's output and its `deps.json`. A host that references `Microsoft.FluentUI.AspNetCore.Components` or its icons with a package reference of its own, to render them on the server, keeps them.

The package's build lands in the host's web root, whether or not the host calls `MapFluentCmsAdmin()`, as static web assets at the paths of the app: `index.html`, `config.json`, `_framework/` and `_content/` among them. So:

- A host with files of its own at those paths, such as a `wwwroot/index.html`, a `wwwroot/config.json`, or the `_content/` of the component library or `_framework/` of a WebAssembly project it references, stops its build on conflicting assets. Move the host's files to other paths, or serve the admin as an app project of the host's own or as the static build of the release ([hosting the admin](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-hosting.md)).
- A host that maps its static web assets itself with `MapStaticAssets()` serves the package's files at its root too, the raw `index.html` and the sample `config.json`, which holds no secret, among them. Serve the host's own files with `UseStaticFiles()` from a folder of their own instead.

On Windows a restore leaves out a file of a package whose path passes 260 characters, and some paths of the build pass 200 under the package folder. With a NuGet cache on a long path, the page still loads and each file left out answers 404, and a publish of the host fails with `MSB3030`, could not copy the file. Keep the cache on a short path, with `NUGET_PACKAGES` if needed.

[Host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md#admin) lists every key, [admin configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-configuration.md) has `config.json`, an admin on another origin and the rate limits, and the [admin overview](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin.md) the first sign-in; section 6.1 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.

Serve nothing of other parties on the admin's origin: the admin keeps the refresh token and unsaved drafts in `sessionStorage`, which every page of the same origin in the same tab can read. A host's own pages, or files users upload, belong on another origin, or the admin on one of its own.
