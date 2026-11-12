# FluentCms-App

A FluentCMS API host, made with the `fluentcms` template of the `FluentCMS.Templates` package:

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms -n FluentCms-App --provider sqlite --sample none
```

| Option | Values | Default |
| --- | --- | --- |
| `--provider` | `sqlite`, `postgresql`, `sqlserver`: the database the host runs on | `sqlite` |
| `--sample` | `none`: one `Article` type; `blog`: the Blog types (articles, authors, tags, an SEO component, hero, quote and gallery blocks), its settings and a seeder that writes sample content | `none` |

## What is here

| File | Holds |
| --- | --- |
| `Program.cs` | `AddFluentCms`, the command entry point of the `fluentcms` tool, `UseFluentCms`, `MapFluentCms` and the OpenAPI document |
| `CmsSetup.cs` | the content types of the project, the data provider and the plugins: media and webhooks |
| `Content/Article.cs`, or `Blog/` | the content types: each class with `[Collection]`, `[Singleton]`, `[Component]` or `[Block]` is one |
| `CmsContextFactory.cs` | the design-time factory that `fluentcms migrations add` builds the content model with |
| `Migrations/Content/` | the content migrations, empty until the first `fluentcms migrations add` |
| `.config/dotnet-tools.json` | the `fluentcms` tool, at the version of the FluentCMS packages |
| `appsettings.json`, `appsettings.Development.json` | the connection string `ConnectionStrings:FluentCms` and the `FluentCms` options |
| `Dockerfile` | a multi-stage image on the chiseled ASP.NET Core runtime, as a non-root user |

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

The image runs the commands of the tool too, so the migrate step of a deployment, such as an init container, is the same image with `fluentcms migrate`. In Production the host stops at start until a signing certificate, `FluentCms__PublicOrigin` and an encryptor of the Data Protection key ring are set, and names each with its setting; set `AllowedHosts` too.

## More

- [Host configuration](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md): the `FluentCms` options, authentication, the production checks
- [Content migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/content-migrations.md): schema sync, `migrations add` and `migrate`
- [Analyzer rules](https://github.com/fluentcms/FluentCMS/blob/main/docs/analyzers.md): the `FCMS0xxx` codes of the build
- [Docker](https://github.com/fluentcms/FluentCMS/blob/main/docs/docker.md): the production settings of an image
