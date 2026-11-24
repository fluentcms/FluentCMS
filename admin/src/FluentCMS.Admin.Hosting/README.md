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
- **An app project of the host's own.** A host that also references an admin app's project, such as one made with `dotnet new fluentcms-admin` to hold a team's own admin halves, serves that app, and the package's build stays out of the host. The package tells the two apart by the files under `_framework/` that a referenced project brings, so the reference itself is the switch: remove it and the host serves the package's build again.

Either way the component library's icon assemblies, which run in the browser only, stay out of the host's output and its `deps.json`.

[Host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md#admin) lists every key; section 6.1 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.

Serve nothing of other parties on the admin's origin: the admin keeps the refresh token and unsaved drafts in `sessionStorage`, which every page of the same origin in the same tab can read. A host's own pages, or files users upload, belong on another origin, or the admin on one of its own.
