# FluentCMS.Admin.Hosting

Serves a [FluentCMS](https://github.com/fluentcms/FluentCMS) admin app from an ASP.NET Core host at `/admin`: the app's files, its `index.html` with the base set, its `config.json` from the host's configuration, the registration of the admin client and the headers of the admin page.

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

The host references the admin app's project, whose files become its static web assets, and turns the admin on with `FluentCms:Admin:Enabled`. [Host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md#admin) lists every key; section 6.1 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design. The prebuilt first-party app inside this package comes later (AQ8).
