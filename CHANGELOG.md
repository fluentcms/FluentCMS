# Changelog

The releases of FluentCMS, newest first. Each links to its notes, which name every change that breaks a plugin or a host built on the release before.

## Versions

Every package, the image of the reference host and `@fluentcms/client` carry one version. A version with a pre-release part, such as `1.0.0-preview.1`, is a preview: it may break the public contract and makes no promise to upgrade a database. From 1.0.0 the contract packages (`FluentCMS.Sdk`, `FluentCMS.Plugins.Abstractions`, `FluentCMS.Admin.Abstractions` and `FluentCMS.Admin.Components`) follow semantic versioning. [Releasing](docs/releasing.md) has the details.

## 1.0.0-preview.2 (25 Nov 2026)

The second preview, the first with the admin, in its three distributions: served at `/admin` by the reference image and by a host of `dotnet new fluentcms`, a prebuilt static build on the GitHub release, and an app of your own from `dotnet new fluentcms-admin`.

[Notes](docs/releases/1.0.0-preview.2.md) and [GitHub release](https://github.com/fluentcms/FluentCMS/releases/tag/v1.0.0-preview.2).

## 1.0.0-preview.1 (16 Nov 2026)

The first public preview of the backend: the engine with the Delivery and Management APIs and the authorization server, the data providers for SQLite, PostgreSQL and SQL Server, the first-party plugins, the `fluentcms` tool and the templates, two minimal clients, and the reference host as an image. The admin, the documentation site and the starters were not part of it.

[Notes](docs/releases/1.0.0-preview.1.md) and [GitHub release](https://github.com/fluentcms/FluentCMS/releases/tag/v1.0.0-preview.1).

## Before the restart

The 0.0.x releases of 2024 belong to the earlier code, which stays under `archive/*` and is not maintained.
