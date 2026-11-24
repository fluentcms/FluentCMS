# FluentCMS.Templates

The `dotnet new` templates of [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET:

- `fluentcms`: an API host on SQLite, PostgreSQL or SQL Server (`--provider`) with the media, search and webhooks plugins, one `Article` type or the Blog sample (`--sample blog`), the admin at `/admin`, the `fluentcms` tool pinned in `.config/dotnet-tools.json`, and a Dockerfile. The admin is the prebuilt build that the `FluentCMS.Admin.Hosting` package carries, with the first-party admin halves; `--admin false` leaves it out.
- `fluentcms-admin`: the admin as a Blazor WebAssembly app of your own, with the administration pages, the rich text editor and the six first-party admin halves as packages, each loaded lazily, and a trimmed publish. Add the admin halves of your own plugins to it, then host it as static files or from a host of `fluentcms`, which serves it at `/admin` in place of the prebuilt build once it references the app's project. It has no option.
- `fluentcms-plugin`: a plugin, with a server half that has a table of its own and its migrations for each provider, a permission, endpoints and a hook, and its tests on the FluentCMS test host; and an admin half with a page, a menu item, a settings page, a dashboard widget and its manifest, and its tests (`--admin false` leaves it out), which goes into an app of `fluentcms-admin`.

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms -n MyCms --provider sqlite --sample blog
dotnet new fluentcms-plugin -n Acme.Notes
dotnet new fluentcms-admin -n MyAdmin
```

A project made by a template references the FluentCMS packages at the version of the templates.
