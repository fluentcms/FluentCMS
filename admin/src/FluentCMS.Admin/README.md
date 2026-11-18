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

The app's `index.html` links the stylesheet of the wrapped set and the app's own bundle of scoped styles, which the framework writes and which brings in the styles of the component library:

```html
<link rel="stylesheet" href="_content/FluentCMS.Admin.Components/cms.css" />
<link rel="stylesheet" href="MyApp.styles.css" />
```

A plugin's admin half is in the app because the app references it, and it loads the first time one of its contributions is used; the app lists it as lazy, and the build stops when it does not:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.Media.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.Media.Admin.wasm" />
</ItemGroup>
```

This preview holds the entry points only; section 2 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
