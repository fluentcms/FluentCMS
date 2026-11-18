# FluentCMS.Templates

The `dotnet new` templates of [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET:

- `fluentcms`: an API host on SQLite, PostgreSQL or SQL Server (`--provider`) with the media and webhooks plugins, one `Article` type or the Blog sample (`--sample blog`), the `fluentcms` tool pinned in `.config/dotnet-tools.json`, and a Dockerfile.
- `fluentcms-plugin`: a plugin, with a server half that has a table of its own and its migrations for each provider, a permission, endpoints and a hook, and its tests on the FluentCMS test host; and an admin half with a page, a menu item, a settings page, a dashboard widget and its manifest, and its tests (`--admin false` leaves it out).

```bash
dotnet new install FluentCMS.Templates
dotnet new fluentcms -n MyCms --provider sqlite --sample blog
dotnet new fluentcms-plugin -n Acme.Notes
```

A project made by a template references the FluentCMS packages at the version of the templates.
