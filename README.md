<p align="center">
  <picture>
    <source srcset="docs/resources/logo-dark.svg" media="(prefers-color-scheme: dark)">
    <img src="docs/resources/logo.svg" alt="FluentCMS" width="120">
  </picture>
</p>

<h3 align="center">FluentCMS</h3>
<p align="center">Created by <a href="https://pournasserian.com/open-source/fluentcms">Amir Pournasserian</a> and <a href="https://momentaj.com/">Momentaj</a>, a Toronto AI engineering firm.</p>

> **Status: first public preview.** `1.0.0-preview.1` is the first public preview of the FluentCMS backend, a headless CMS for .NET: its packages on nuget.org and the image of its reference host on the GitHub container registry. The contract can still break between previews, and a preview makes no promise to upgrade a database. The [release notes](docs/releases/1.0.0-preview.1.md) name its limits.

## What the preview holds

- **The backend.** The engine with the Delivery and Management APIs and the authorization server (`FluentCMS.Core`), the contract packages `FluentCMS.Sdk` and `FluentCMS.Plugins.Abstractions`, the data providers for SQLite, PostgreSQL and SQL Server, and the reference host as an image.
- **Its first-party plugins.** Media (with S3 and Azure Blob storage), webhooks, GraphQL, MCP, AI assist, multi-site and search.
- **The CLI and the templates.** The `fluentcms` tool, and the `dotnet new` templates `fluentcms` for a host and `fluentcms-plugin` for a plugin.
- **Two minimal clients.** `FluentCMS.Client` for C# and `@fluentcms/client` for TypeScript, which generated code compiles against. For this preview `@fluentcms/client` is attached to the [GitHub release](https://github.com/fluentcms/FluentCMS/releases/tag/v1.0.0-preview.1) as a `.tgz`, unless it is published to npm.

The admin, the documentation site and the starters are not part of the preview.

## Quick start

With the .NET 10 SDK, from nuget.org:

```bash
dotnet new install FluentCMS.Templates@1.0.0-preview.1
dotnet new fluentcms -n MyCms --sample blog
cd MyCms
dotnet run
```

The host runs on SQLite with the Blog sample, at `https://localhost:5443`. The [template's README](templates/fluentcms/README.md) covers PostgreSQL and SQL Server, migrations, the first administrator and an API key.

With Docker, the compose file of the reference host runs the published image `ghcr.io/fluentcms/fluentcms:1.0.0-preview.1` on PostgreSQL, with its media in MinIO. In `src/FluentCMS.Host` of a clone of this repository, copy `.env.example` to `.env`, fill in every empty value, set `FLUENTCMS_IMAGE=ghcr.io/fluentcms/fluentcms:1.0.0-preview.1`, and start:

```bash
docker compose pull
docker compose up -d --no-build
```

`http://localhost:8080/health/ready` answers `Healthy` once the host is ready. [Run with Docker](docs/docker.md) covers the first administrator, a key and what to change for production.

## Documentation

- [Host configuration](docs/host-configuration.md), [Run with Docker](docs/docker.md) and [Reverse proxy](docs/reverse-proxy.md)
- [Content migrations](docs/content-migrations.md), [System migrations](docs/system-migrations.md), [Plugin migrations](docs/plugin-migrations.md) and [Backups](docs/backups.md)
- [The `fluentcms` tool](src/FluentCMS.Cli/README.md), [the templates](templates/README.md) and [analyzer rules](docs/analyzers.md)
- Each package's README beside its project, under [`src`](src) and [`src/plugins`](src/plugins), and the [TypeScript client](clients/typescript/README.md)
- The [backend technical specification](docs/planning/backend-technical-spec.md), and [CONTRIBUTING.md](CONTRIBUTING.md) for how to take part

## Earlier code

Earlier work stays available, read-only and unmaintained, under `archive/*` branches and tags.

## Community

- [Discord](https://discord.gg/WyqYuC6YbY)
- [Contributing](CONTRIBUTING.md) and [Security policy](SECURITY.md)

## NuGet packages

FluentCMS packages are published on nuget.org under the [FluentCMS profile](https://www.nuget.org/profiles/FluentCMS), with the `FluentCMS.*` prefix. The package named just [`FluentCMS`](https://www.nuget.org/packages/FluentCMS) on nuget.org belongs to a different project.

## License

[MIT](LICENSE)
