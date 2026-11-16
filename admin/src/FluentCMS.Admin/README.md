# FluentCMS.Admin

The [FluentCMS](https://github.com/fluentcms/FluentCMS) admin for a Blazor WebAssembly app: the shell, sign-in, the Management API client, the generated lists and forms, and the pages of core's groups. It reaches the server only over HTTP.

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AdminApp>("#app");
await builder.AddFluentCmsAdmin();
await builder.Build().RunAsync();
```

This preview holds the entry points only; section 2 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
