# 0003 Put the plugin contracts that need EF Core or ASP.NET Core in FluentCMS.Plugins.Abstractions

## Status

Accepted.

## Date

2026-11-06

## Context

A plugin compiles against `FluentCMS.Sdk` and never against `FluentCMS.Core` (spec 2.4, 3.4). The SDK has no dependency on EF Core or ASP.NET Core beyond the `Microsoft.Extensions.*` abstractions (spec 4.1). [#2829](https://github.com/fluentcms/FluentCMS/issues/2829) lets a plugin contribute endpoints, tables, permissions, settings, jobs, event handlers and error codes. Three of these need types the SDK cannot name:

- A model contributor configures an EF Core `ModelBuilder`.
- A migration stream is EF Core migrations, which derive from `Migration` and name their context in `[DbContext]`.
- Endpoint mapping returns an ASP.NET Core `RouteGroupBuilder`. ASP.NET Core ships only as the `Microsoft.AspNetCore.App` framework, so no small package gives the SDK that type.

Options considered:

- **A. Reference EF Core and the ASP.NET Core framework from the SDK.** This breaks 4.1, and every plugin test project pulls in both.
- **B. SDK-only shapes**, such as endpoints as delegates and tables described without EF Core. This hides what a plugin needs anyway. A plugin with tables writes EF Core migrations, and a plugin with endpoints writes minimal API handlers, so both reference those stacks already. Every EF Core and routing feature would also need its own SDK wrapper.
- **C. A second contract package** that holds only the contracts that need those types. It references the SDK, EF Core Relational and the ASP.NET Core framework.

## Decision

We will adopt option C:

- **Package.** `FluentCMS.Plugins.Abstractions` references `FluentCMS.Sdk`, `Microsoft.EntityFrameworkCore.Relational` and the `Microsoft.AspNetCore.App` framework, and no implementation package. It is semver-guaranteed like the SDK and tracks its public API with the analyzers. A plugin references the SDK, and the Abstractions package when it has tables or endpoints. Core references the package and implements it. A plugin never references Core, which the architecture tests check.
- **SDK members.** `IPluginServiceBuilder` mirrors the core builders: `AddFieldType`, `AddPermissions`, `AddSettings`, `AddJob`, `AddEventHandler`, `AddOutboxHandler`, `AddActivity` and `AddErrorCode`. A plugin refuses a request with `PluginException(code, message)`. These need no EF Core or ASP.NET Core types.
- **Abstractions members.** The package adds extension methods on the SDK builders, in the `FluentCMS.Plugins` namespace, so a plugin writes `plugin.AddModel<T>()` and `app.MapGroup(prefix)` as spec 4.13 shows. Core's builders implement internal interfaces of the package, and the extension methods call them. The package also holds:
  - `IPluginModelContributor`, which configures a `ModelBuilder`;
  - `PluginDbContext<TPlugin>`, the context of a plugin's stream and of its runtime queries.
- **Stream context.** A plugin's migrations name `PluginDbContext<TPlugin>` in `[DbContext]`, so each plugin has its own context type with no class of its own to write. Core configures the context with:
  - the provider and connection;
  - the history table `cms_{plugin}__migrations`;
  - the plugin's model contributors;
  - the provider's conventions.

  The same contributors join `CmsDbContext`, excluded from its migrations.
- **Providers.** One migration set per plugin assembly, run on SQLite in M3. Per-provider sets in one plugin assembly are left to [#2711](https://github.com/fluentcms/FluentCMS/issues/2711), with the other PostgreSQL and SQL Server work.

## Consequences

- The SDK stays light, and a plugin with no tables or endpoints needs only the SDK.
- A plugin with tables or endpoints references two contract packages instead of one. The sample plugin of the tests references the SDK and the Abstractions package, and not Core, which is what the M3 exit criterion checks.
- Spec 4.1 moves `IPluginModelContributor` to the new package. The package map (2.4) and the architecture tests (3.4) name it.
- The extension methods throw when the builder is not core's. A plugin's unit test that fakes `IPluginServiceBuilder` cannot call them, and tests those contributions through the test host instead.
- `PluginDbContext<TPlugin>` is generic, so `dotnet ef` needs a design-time factory for the closed type, and it is the only context in the plugin's project. The first plugin package that ships migrations will document the steps, as `docs/system-migrations.md` does for the system stream.
