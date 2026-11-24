# FluentCms-App

<!--#if (admin) -->
A FluentCMS API host with the admin, made with the `fluentcms` template of the `FluentCMS.Templates` package:
<!--#else -->
A FluentCMS API host, made with the `fluentcms` template of the `FluentCMS.Templates` package:
<!--#endif -->

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms -n FluentCms-App --provider sqlite --sample none
```

| Option | Values | Default |
| --- | --- | --- |
| `--provider` | `sqlite`, `postgresql`, `sqlserver`: the database the host runs on | `sqlite` |
| `--sample` | `none`: one `Article` type; `blog`: the Blog types (articles, authors, tags, an SEO component, hero, quote and gallery blocks), its settings and a seeder that writes sample content | `none` |
<!--#if (admin) -->
| `--admin` | `true`: the admin at `/admin`, from the prebuilt build of `FluentCMS.Admin.Hosting`; `false`: the API host only | `true` |
<!--#endif -->

## What is here

| File | Holds |
| --- | --- |
<!--#if (admin) -->
| `Program.cs` | `AddFluentCms` with `AddAdmin`, the command entry point of the `fluentcms` tool, `UseFluentCms`, `MapFluentCms`, the OpenAPI document and `MapFluentCmsAdmin` |
<!--#else -->
| `Program.cs` | `AddFluentCms`, the command entry point of the `fluentcms` tool, `UseFluentCms`, `MapFluentCms` and the OpenAPI document |
<!--#endif -->
| `CmsSetup.cs` | the content types of the project, the data provider and the plugins: media, webhooks and search |
| `Content/Article.cs`, or `Blog/` | the content types: each class with `[Collection]`, `[Singleton]`, `[Component]` or `[Block]` is one |
| `CmsContextFactory.cs` | the design-time factory that `fluentcms migrations add` builds the content model with |
| `Migrations/Content/` | the content migrations, empty until the first `fluentcms migrations add` |
| `.config/dotnet-tools.json` | the `fluentcms` tool, at the version of the FluentCMS packages |
<!--#if (admin) -->
| `appsettings.json`, `appsettings.Development.json` | the connection string `ConnectionStrings:FluentCms` and the `FluentCms` options, among them `FluentCms:Admin:Enabled`, which turns the admin on |
<!--#else -->
| `appsettings.json`, `appsettings.Development.json` | the connection string `ConnectionStrings:FluentCms` and the `FluentCms` options |
<!--#endif -->
| `Dockerfile` | a multi-stage image on the chiseled ASP.NET Core runtime, as a non-root user |
| `.dockerignore` | what stays out of the image's build context: build output, the local database, and secrets and local settings such as `.env`, `*.pfx`, `*.key` and `*.local.json` |

The project references `FluentCMS.Generators`, which writes a typed filter and sort builder per content type (`ArticleFields.Title.Contains("x")`), and `FluentCMS.Analyzers`, which reports a wrong schema attribute in the editor. Both run in the build only.

## Run it in Development

On SQLite nothing else is needed:

```bash
dotnet run
```

The host listens on `https://localhost:5443` and `http://localhost:5080`, with the ASP.NET Core development certificate (`dotnet dev-certs https --trust`); sign-in and tokens need HTTPS. In Development on SQLite, schema sync creates the tables of the content types in `fluentcms.db` and adds a field's column when you add the field, so you change a class and run again. The OpenAPI document is at `/openapi/v1.json` with its Scalar page at `/scalar`, and `/health/ready` answers once the host is ready. With `--sample blog`, the seeder writes the sample content at the first start.

On PostgreSQL or SQL Server, `appsettings.Development.json` names a local server without a password. Keep the password out of the project, in user secrets or in the environment (`ConnectionStrings__FluentCms`):

```bash
dotnet user-secrets set "ConnectionStrings:FluentCms" "Host=localhost;Database=fluentcms;Username=postgres;Password=<password>"
```

Those names are the administrators that a local server or its container image starts with, `postgres` and `sa`. Outside development, connect with a login of the application's own.

Schema sync is for SQLite only, so a server database gets its tables from migrations, as below, before the host starts.

## The fluentcms tool

The tool is pinned in `.config/dotnet-tools.json`. Restore it once, and run its commands in the project directory:

```bash
dotnet tool restore
dotnet fluentcms --help
```

A command runs inside the host, with its configuration, provider and plugins, so `Program.cs` keeps the call to `RunFluentCmsCommandAsync` between `Build()` and `Run()`. It runs in the environment of the shell, Production unless `DOTNET_ENVIRONMENT` says otherwise, so the commands below pass `--environment Development` to read `appsettings.Development.json` and the user secrets, as `dotnet run` does. Against another database, leave it out and set `ConnectionStrings__FluentCms`.

## A server database: migrations

A migration set belongs to one provider, so the template ships none: the first one is made for the provider you chose.

```bash
dotnet fluentcms migrations add Initial
dotnet fluentcms migrate --environment Development
dotnet run
```

`migrations add` writes the content migration of the model to `Migrations/Content`, and refuses a change that loses data unless you pass `--allow-destructive`. `migrate` applies what is pending in every stream: the system tables, the plugins' tables and the content tables. Add a migration with each change of the content types, and run `migrate` before the new version starts; `fluentcms migrate --check` exits with 1 while one is pending.

On SQLite the same migrations are how a production database is made, from a new database: one that schema sync changed has no content history, so the first migration does not apply to it. On SQLite the host applies its migrations at start (`FluentCms:Database:MigrateOnStartup`, `true` on SQLite and `false` on the servers).

## The first administrator and a key

Once the system tables exist, after `fluentcms migrate` or a first start:

```bash
dotnet fluentcms user create --email you@example.com --role "Super Admin" --password-stdin --environment Development
dotnet fluentcms apikey create --name website --class management --role "Super Admin" --environment Development
```

The password is read from standard input, so it stays out of the shell history. The key is shown once; send it as `Authorization: ApiKey <key>`.
<!--#if (admin) -->

## The admin

With the host running, open `https://localhost:5443/admin/` and sign in as the administrator made above. The admin is a Blazor WebAssembly app that runs in the browser and calls the Management API of this host, on its origin, so it needs no CORS setting and no client to configure: `cms.AddAdmin()` registers the client `fluentcms-admin`, and `app.MapFluentCmsAdmin()` serves the app at `/admin`, its `index.html` with the base set and its `config.json` built from the `FluentCms:Admin` settings, such as `Branding:Name` and `PreviewOrigins` ([host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md#admin)). `FluentCms:Admin:Enabled` in `appsettings.json` turns it on; set to `false`, `/admin` answers 404 and the client is deleted at the next start, which signs every admin user out.

The app is the prebuilt build that `FluentCMS.Admin.Hosting` carries: the shell, the administration pages, the rich text editor and the first-party admin halves, of the media, webhooks, MCP, AI assist, multi-site and search plugins. Its files become static web assets of this host, served by `dotnet run` and copied into `wwwroot` by `dotnet publish`, whether or not `MapFluentCmsAdmin()` is called. So:

- The host has no files of its own at the app's paths: a `wwwroot/index.html` or `wwwroot/config.json`, the `_content/` of the component library, or the `_framework/` of a WebAssembly project it references, stops its build on conflicting assets.
- A call of `app.MapStaticAssets()` of the host's own would serve the app's files at the root too, the raw `index.html` and the sample `config.json` among them; serve files of the host's own with `UseStaticFiles()` from a folder of their own.
- On Windows a restore leaves out a file of a package whose path passes 260 characters, and some paths of the build pass 200 under the package folder. With a NuGet cache on a long path, the page loads and each file left out answers 404, and a publish fails with `MSB3030`. Keep the cache on a short path, with `NUGET_PACKAGES` if needed.

For an admin with halves of your own, such as the admin half of a plugin made with `dotnet new fluentcms-plugin`, make an admin app with `dotnet new fluentcms-admin`, add the halves to it, and reference its project from this host:

```bash
dotnet new fluentcms-admin -n MyAdmin -o ../MyAdmin
dotnet add reference ../MyAdmin/MyAdmin.csproj
```

The host then serves that app at `/admin` in place of the prebuilt build, with the same `config.json`; remove the reference and it serves the prebuilt build again. The `Dockerfile` builds from this folder alone, so with a reference to an app outside it, build the image from a folder that holds both and copy both projects.

Serve nothing of other parties on this host's origin: the admin keeps its refresh token and unsaved drafts in `sessionStorage`, which every page of the same origin in the same tab can read.
<!--#endif -->

## A front end

The types of a front end come from the schema of the host:

```bash
dotnet fluentcms generate typescript --output ../web/src/cms
```

`generate csharp` does the same for a .NET client, and `generate openapi` writes the OpenAPI document.

## Docker

```bash
docker build -t fluentcms-app .
docker run --rm fluentcms-app fluentcms migrate
```

The image runs the commands of the tool too, so the migrate step of a deployment, such as an init container, is the same image with `fluentcms migrate`. In Production, as in every environment but Development, the host stops at start until a signing certificate, `FluentCms__PublicOrigin` and an encryptor of the Data Protection key ring are set, and names each with its setting; set `AllowedHosts` too.
<!--#if (admin) -->
The image serves the admin at `/admin`, whose sign-in addresses and `config.json` are built from `FluentCms__PublicOrigin` outside Development.
<!--#endif -->

## More

- [Host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md): the `FluentCms` options, authentication, the startup refusals
- [Content migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/content-migrations.md): schema sync, `migrations add` and `migrate`
- [Analyzer rules](https://github.com/fluentcms/FluentCMS/blob/main/docs/analyzers.md): the `FCMS0xxx` codes of the build
- [Docker](https://github.com/fluentcms/FluentCMS/blob/main/docs/docker.md): the production settings of an image
