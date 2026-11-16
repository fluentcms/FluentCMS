# FluentCMS.Admin

The [FluentCMS](https://github.com/fluentcms/FluentCMS) admin for a Blazor WebAssembly app: the shell, sign-in, the Management API client, the generated lists and forms, and the pages of core's groups. It reaches the server only over HTTP.

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AdminApp>("#app");
await builder.AddFluentCmsAdmin();
await builder.Build().RunAsync();
```

The app's project file must load all globalization data. The admin sets the interface culture at start, `en` for a browser in `en-US`, and with only the culture data of the browser's language the runtime then stops the app with a blank page:

```xml
<PropertyGroup>
  <BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>
</PropertyGroup>
```

This preview holds the entry points only; section 2 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
