# 0009 Give plugins a key-value store with compare-and-set in `cms_plugin_store`

## Status

Accepted.

## Date

2026-11-26

## Context

[#3606](https://github.com/fluentcms/FluentCMS/issues/3606), found in [#3484](https://github.com/fluentcms/FluentCMS/issues/3484) and [#3603](https://github.com/fluentcms/FluentCMS/issues/3603), asks for a place where a plugin keeps the state of a job, such as where a backfill stands, across the instances that share a database.

What the code did before:

- **The media backfill** (`media.dimensions.backfill`, [#3482](https://github.com/fluentcms/FluentCMS/issues/3482)) kept its cursor in row 2 of `cms_media_signing_key`, the signing key's table and the only store of the plugin with a conditional update and no side effect. A start claimed the check by inserting that row, or by moving the time of a stale claim; each run moved the cursor on with an update conditional on the cursor it read.
- **What a plugin has.** Its own tables through `PluginDbContext<TPlugin>`, in a migration stream of its own; its settings through `ISettings<T>`, which raise an event and write an activity row; and `IPluginState`, which says whether a plugin is enabled for a site. None of these is a small conditional store: a table of its own costs every plugin a model and three migration sets, and settings are for people.
- **The model.** A table in core uses the repository's one model slot and, before 1.0.0, folds into the system stream's `Initial`; a database made by an earlier build is then refused with `FCMS1308` and recreated ([system migrations](../system-migrations.md)).

## Decision

- **The interface.** `IPluginStore<TPlugin>` in the namespace `FluentCMS.Plugins` of `FluentCMS.Sdk`, beside `IPluginState`, whose name is taken by the enabled state. A value reads as `PluginStoreValue(Key, Value, Version, UpdatedAt)`. The members are `GetAsync(key)`, `TryAddAsync(key, value)`, which adds only when the key has none, `TrySetAsync(key, value, version)`, which writes only at the version read, and `DeleteAsync(key, version?)`. A write that loses answers `null` or `false`, never an exception, since losing is the expected outcome for one of two instances; an argument outside the limits throws `ArgumentException` before any statement.
- **Scope.** A plugin resolves the store of its own type from a scope, as it does `PluginDbContext<TPlugin>`. Core registers it as an open generic scoped service and finds the plugin's name in the registry by `typeof(TPlugin)`, so a type that is not a plugin of the host is refused when it is resolved. No member takes a plugin's name, so no member reaches another plugin's rows.
- **Limits.** A key has 1 to 128 characters: lower case letters and digits, in groups joined by a single `.`, `-`, `_` or `:`. Lower case ASCII compares the same under every collation of the three providers. A value is text of at most 4,000 characters, which SQL Server keeps in the row; a plugin that keeps a structure serializes it.
- **The table.** `cms_plugin_store (plugin, key, value, version, updated_at)` with the primary key (`plugin`, `key`): `plugin` and `key` are text of 32 and 128 characters, `value` of 4,000, `version` a 64-bit integer that is 1 on insert and one more at each write, and `updated_at` the time of the last write by the host's clock. It folds into the system `Initial` of the three providers.
- **One statement per call.** An add is one insert, and a key that has a value is told by the provider's unique violation. A write is one `UPDATE ... SET value, version = version + 1, updated_at WHERE plugin, key and version = @read`, and a delete one `DELETE` with the same filter when a version is given; one affected row is a win. Each provider makes the second of two such statements on one row see the first's result: SQLite has one writer, PostgreSQL checks the filter again on the row it locked, and SQL Server reads the latest committed row for an update even under `READ_COMMITTED_SNAPSHOT` ([ADR 0005](0005-sql-server-read-committed-snapshot.md)). `updated_at` is never compared for equality; the version is what a write compares.
- **Outside the caller's transaction.** Each call runs on a context of its own and commits on its own, so a call while the caller has a transaction open on a core or plugin context is refused with `InvalidOperationException`, as `ISettings.SetAsync` is: a rollback of the transaction would not take the write back, and the caller would believe it had.
- **Per instance.** Values belong to the instance, not to a site, and nothing is cached: a value another instance wrote reads at once.
- **Who deletes.** Only the plugin, through `DeleteAsync`. Core never does: a plugin that is no longer registered keeps its rows, which say nothing then, as its rows of `cms_plugin_site` do. Nothing of the store goes into a transfer package or an export.
- **The media backfill.** Its cursor moves to the media plugin's value `media.dimensions.backfill`. A start claims the check by adding the value, or by writing a stale one again at its version, and the claim is the version it wrote; a run writes the next cursor at the version it read.

## Not taken

- **A JSON column.** PostgreSQL's `jsonb` gives back other text than was written, and a check of the JSON on every write costs for a value that is often not JSON, such as the cursor.
- **Bytes.** Text covers what a job keeps, and reads in a query or a log; the providers' binary types also differ more than their text types.
- **A per-site scope.** Deferred until a plugin needs one. It would need a site column with a foreign key that cascades, so that the deletion of a site takes its values, and a key that cannot be the primary key while the site is nullable. A plugin that needs it now names the site in the key and removes its values on `SiteDeletedEvent`.
- **Joining the caller's transaction.** It would tie a call to a connection of the caller's context, which on SQLite is a second writer, and make the outcome of a compare-and-set depend on a commit that comes later.
- **A migration step for the media marker.** A preview upgrades no database: one made by an earlier build lacks `cms_plugin_store` and is refused with `FCMS1308` before anything is applied, so no database that this build runs on can hold row 2. A step would also need a second migration in the media stream, which has only `Initial`.

## Consequences

- A plugin keeps the state of a job without a table of its own, and two instances that write it at once cannot both win.
- Every database made before this change is refused with `FCMS1308`, naming `cms_plugin_store`, and recreated. The token keys of [ADR 0008](0008-token-key-rotation.md) already refuse every database of `1.0.0-preview.2`, so the next preview breaks no database that would otherwise start.
- On a recreated database the media check runs once more, over a library that is empty then.
- The media check changes in one case: when a start takes over a stale claim while a run of the old chain is still reading its batch, that run loses its write and enqueues nothing, and the new chain reads that batch again. Before, the two chains took turns on the row. Writing a width and height again is harmless.
- `plugin_store` joins the system tables, so a content type may not take that name.
- The store is not a security boundary between plugins: code that names another plugin's type resolves that plugin's store, as it can resolve that plugin's `PluginDbContext<TPlugin>`. Plugins are compiled into the host and trusted as its code is.
