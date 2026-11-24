# FluentCms-AdminApp

The FluentCMS admin as a Blazor WebAssembly app of your own, made with the `fluentcms-admin` template of the `FluentCMS.Templates` package:

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms-admin -n FluentCms-AdminApp
```

It is the app that `FluentCMS.Admin.Hosting` carries prebuilt, `FluentCMS.Admin.App` of the FluentCMS repository, built from packages: the shell, the administration pages, the rich text editor and the six first-party admin halves, of the media, webhooks, MCP, AI assist, multi-site and search plugins. Make it when the admin needs a half the prebuilt build does not have, such as the half of a plugin of your own, or to build and host the admin yourself. The template has no option.

## What is here

| File | Holds |
| --- | --- |
| `FluentCms-AdminApp.csproj` | the package references of the admin and of each half, each half with its `BlazorWebAssemblyLazyLoad` line, and the trimmed publish |
| `Program.cs` | `AddFluentCmsAdmin`, which reads `config.json` and the halves, and the admin's root component |
| `wwwroot/index.html` | the page, with its `<base href>` |
| `wwwroot/config.json` | where the API is and which client signs in, here a host of `dotnet new fluentcms` run in Development at `https://localhost:5443` |
| `Properties/launchSettings.json` | `dotnet run` and `dotnet watch` at `https://localhost:5201` |

## Run it against a local host

The app at `https://localhost:5201` is on another origin than the API host, which then needs two settings (section 6.4 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md#64-an-admin-on-another-origin)): the app's origin under CORS, and the admin's client with the app's addresses. In the `appsettings.Development.json` of the host:

```json
{
  "FluentCms": {
    "Security": { "Cors": { "Management": { "AllowedOrigins": [ "https://localhost:5201" ] } } },
    "Clients": {
      "Applications": {
        "fluentcms-admin": {
          "DisplayName": "FluentCMS admin",
          "RedirectUris": [ "https://localhost:5201/authentication/login-callback" ],
          "PostLogoutRedirectUris": [ "https://localhost:5201/" ],
          "Scopes": [ "openid", "profile", "email", "offline_access", "management" ]
        }
      }
    }
  }
}
```

A client configured under `fluentcms-admin` wins whole over the one that `FluentCMS.Admin.Hosting` registers, so a host that also serves the admin at `/admin` lists those addresses beside these: `https://localhost:5443/admin/authentication/login-callback` and `https://localhost:5443/admin/`. Then run the host, and the app with hot reload:

```bash
dotnet watch
```

Sign in at `https://localhost:5201/` with a user of the host, such as the first administrator that `dotnet fluentcms user create` makes. Both need https, with the ASP.NET Core development certificate (`dotnet dev-certs https --trust`).

## Add an admin half of your own

Reference the half, a project or the package that its `dotnet pack` makes, such as the admin half of `dotnet new fluentcms-plugin`, and list its assembly as lazy, so that the browser downloads it the first time one of its pages is opened:

```xml
<ItemGroup>
  <ProjectReference Include="../Acme.Reviews/src/Acme.Reviews.Admin/Acme.Reviews.Admin.csproj" />
  <BlazorWebAssemblyLazyLoad Include="Acme.Reviews.Admin.wasm" />
</ItemGroup>
```

The build writes the `fluentcms-admin.json` of every half it references into `fluentcms-admin.plugins.json`, which the admin reads at start, and stops when a half is not listed as lazy, naming the line to add. The admin needs no code for the half: in a site where its server plugin is enabled, the sidebar shows its menu items to a user who holds their permissions. A half the admin cannot use, such as one whose admin range does not hold the running admin, is left out and listed on `/system/plugins`.

To leave out a first-party half, remove its two lines. The admin shows a half's contributions only in a site where its server plugin is enabled, so a half whose plugin the host does not run shows nothing and only costs its download when a page of it is opened.

## Publish

```bash
dotnet publish -c Release
```

The publish is trimmed, and a trim warning of the app or of a half it references as a project fails it, so that code the trimmer cannot follow is found here rather than in the browser. The admin's halves are not trimmable, so their types stay whole. The warnings of an assembly of a package are one `IL2104` that names the assembly, which stays a warning; the framework's `Microsoft.AspNetCore.Components` and `Microsoft.JSInterop` report one each. The published app is the `wwwroot` folder of the publish, with a Brotli and a gzip copy beside each file but `index.html` and `config.json`.

The app holds FluentCMS's code, under the MIT licence, and the code of other parties whose notices are in the `THIRD-PARTY-NOTICES.md` of the `FluentCMS.Admin` package. Ship the licence and the notices with the app, as the prebuilt build does beside its `index.html`.

## Host it

As static files, from any static host or CDN: edit `config.json` and the `<base href>` of `index.html` of the published app for where it is served, and have the host fall back to `index.html`, keep `index.html` and `config.json` from every cache and send the admin's headers. The [hosting guide](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-hosting.md) has what any host must do and recipes, and the two settings the API host needs, as above with the app's public origin.

From the API host, at `/admin`: a host with `FluentCMS.Admin.Hosting`, such as one of `dotnet new fluentcms`, serves this app in place of the prebuilt build once it references this project:

```bash
dotnet add ../MyCms/MyCms.csproj reference FluentCms-AdminApp.csproj
```

The host's build and publish compile the app, and the host serves its files under `/admin`, `index.html` with its base set and `config.json` from the host's `FluentCms:Admin` settings, so neither file of `wwwroot` is edited, and the admin is on the API's origin with no CORS setting and no client to configure. The package tells this app by the `fluentcms-admin.plugins.json` of its build, so removing the reference serves the prebuilt build again. The icon assemblies of the component library, which only the browser loads, stay out of the host's output. The host must have no files of its own at the app's paths, such as a `wwwroot/index.html`, a `wwwroot/config.json` or the `_framework/` of another WebAssembly project, or its build stops on conflicting assets. `dotnet watch` on the host rebuilds the app as its files change.

On Windows a restore leaves out a file of a package whose path passes 260 characters, and some paths of the admin's packages pass 200 under the package folder: with a NuGet cache on a long path, the page loads and each file left out answers 404, and a publish fails with `MSB3030`. Keep the cache on a short path, with `NUGET_PACKAGES` if needed.

## More

- [Admin overview](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin.md): the distributions and the first sign-in
- [Admin configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-configuration.md): every member of `config.json`, an admin on another origin and the client `fluentcms-admin`
- [Writing an admin half](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-plugins.md): a half of your own, from `dotnet new fluentcms-plugin`
- [Translating the admin](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-translating.md): the resource files of a translation and the pseudo-cultures
- [Host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md#admin): the `FluentCms:Admin` settings and what the host serves at `/admin`
- [Hosting the admin](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-hosting.md): the static build on another origin
- [Admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md): the plugin contract of an admin half (section 5) and the distributions (section 6)
