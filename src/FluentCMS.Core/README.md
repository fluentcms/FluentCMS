# FluentCMS.Core

The engine of [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET, and its HTTP surfaces: the schema, content storage and queries, versions, publishing and scheduling, localization and sites, admin users, roles and API keys, the authorization server and its hosted pages, the activity log, events, hooks and jobs, the plugin host, and the Delivery and Management APIs with their OpenAPI document.

A host adds it to an ASP.NET Core application with one data provider, `FluentCMS.Data.Sqlite`, `FluentCMS.Data.PostgreSql` or `FluentCMS.Data.SqlServer`:

```csharp
builder.Services.AddSingleton(services => new SchemaBuilder(services.GetRequiredService<IFieldTypeRegistry>())
    .AddContentTypesFromAssembly(typeof(Program).Assembly)
    .Build());
builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("FluentCms")));

var app = builder.Build();
app.UseFluentCms();
app.MapFluentCms();
app.Run();
```

`dotnet new fluentcms` from `FluentCMS.Templates` makes such a host, and [host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md) lists its options. To run FluentCMS without compiling anything, use the image `ghcr.io/fluentcms/fluentcms` ([Docker](https://github.com/fluentcms/FluentCMS/blob/main/docs/docker.md)).
