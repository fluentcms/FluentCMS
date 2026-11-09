# 0001 Use property bags for content tables

## Status

Accepted. Confirmed on SQLite by the spike, and on PostgreSQL 15, SQL Server 2019 and SQL Server 2025 by [#2711](https://github.com/fluentcms/FluentCMS/issues/2711); see [Verified on the server providers](#verified-on-the-server-providers).

## Date

2026-11-02, updated 2026-11-08 with the results of #2711.

## Context

Q5 in the [backend specification](../planning/backend-technical-spec.md) chose EF Core shared-type (property bag) entity types over source-generated row classes, subject to a spike. Q7 limits JSON filtering to block-type presence and scalar paths one level deep. [#2681](https://github.com/fluentcms/FluentCMS/issues/2681) is that spike, and Q19 reduced it to SQLite.

The evidence is the 52 tests in `tests/FluentCMS.Storage.Spike.Tests`, which [#2736](https://github.com/fluentcms/FluentCMS/issues/2736) removed once the real storage landed (see [Where the evidence lives now](#where-the-evidence-lives-now)). They ran on an in-memory database with EF Core 10.0.14, Microsoft.Data.Sqlite 10.0.14 and the bundled SQLite 3.53.3. The SQL below is asserted by those tests unless it says otherwise.

## Decision

We will build the content tables from `SchemaModel` as shared-type entity types, as Q5 says, and apply the [specification changes](#changes-to-the-specification) below before [#2688](https://github.com/fluentcms/FluentCMS/issues/2688) builds the real tables.

## Evidence

### Property bags

Nothing in the spike needed a generated class.

- `cms_article`, `cms_article_i18n`, `cms_document` and `cms_reference` are `Dictionary<string, object>` entity types built from a runtime schema record.
- A dictionary may omit nullable columns; they read back as null. Changing one value updates one column.
- Each schema needs its own model, so `IModelCacheKeyFactory` is replaced with one keyed on the schema.
- In a fresh process, the first model took about 320 ms, which includes EF start-up. After that, 50 types of 20 fields (103 entity types) took about 150 ms and 200 types took 610 to 710 ms. These are two runs by hand on a development machine, not a test. #2688 targets under one second for 50 types.

### JSON filters

JSON columns are plain text columns mapped as `string`. EF's `ToJson()` does not fit, because a block list is a union with no single shape. Filters use EF function mappings instead: `json_each` as a keyless table-valued function, and `json_extract` as a scalar function with text and integer variants.

Block-type presence:

```sql
SELECT "c"."document_id"
FROM "cms_article" AS "c"
WHERE EXISTS (
    SELECT 1
    FROM json_each("c"."sections") AS "j"
    WHERE json_extract("j"."value", '$.type') = @blockType)
```

The other filters differ only in the predicate:

| Filter | SQL |
| --- | --- |
| Scalar path inside a block | `... WHERE json_extract("j"."value", '$.type') = @blockType AND json_extract("j"."value", '$.heading') = @heading)` |
| Scalar path on a JSON column | `WHERE json_extract("c"."metadata", '$.audience') = @audience` |
| String containment on a path | `WHERE instr(json_extract("c"."metadata", '$.audience'), @term) > 0` |
| Array containment on a path | `WHERE EXISTS (SELECT 1 FROM json_each(json_extract("c"."metadata", '$.labels')) AS "j" WHERE "j"."value" = @label)` |

A small filter AST compiled at runtime to a LINQ expression, as section 4.4 describes, produces the same SQL and composes with ordinary columns:

```sql
WHERE "c"."state" = 1 AND "c"."category" = @Value AND EXISTS (
    SELECT 1
    FROM json_each("c"."sections") AS "j"
    WHERE json_extract("j"."value", '$.type') = @Value1 AND json_extract("j"."value", '$.heading') = @Value2) AND (json_extract("c"."metadata", '$.audience') <> @Value3 OR json_extract("c"."metadata", '$.audience') IS NULL)
```

What the tests show about these filters on SQLite:

- A block filter is a full table scan (`SCAN c`).
- `json_extract` returns the JSON value's own type, so comparing a JSON number with the text `'12'` finds nothing.
- `instr` is case-sensitive.
- One row with malformed JSON makes the whole filter query throw. `CHECK (json_valid(col))` rejects such a row on insert (extended error code 275).

### Partial unique index

Supported and enforced. `HasFilter("state = 1")` on a shared-type entity reaches `EnsureCreated`, the scaffolded migration and the migration script:

```sql
CREATE UNIQUE INDEX "ux_cms_article_i18n_slug" ON "cms_article_i18n" ("site_id", "locale", "slug") WHERE state = 1
```

- A second published row with the same site, locale and slug fails with `DbUpdateException` around `SqliteException` (code 19, extended code 2067).
- Drafts may share a slug, and the same slug is allowed in another locale or site.
- The query plan uses the index (`SEARCH c USING INDEX ux_cms_article_i18n_slug (site_id=? AND locale=? AND slug=?)`) for a literal `state`, for `EF.Constant` and for a bound parameter whose value is 1. It does not use it for a bound value of 0. SQLite therefore does not require an inlined `state`.
- NULLs are distinct in a unique index, so a null `site_id` or `locale` defeats it. Global types with a null `site_id` work once `site_id` is left out of the index, and an empty-string `locale` in `cms_reference` keeps its key enforced.

### Migration generation

It works from the property-bag model, in process and from the CLI.

- `IMigrationsScaffolder`, reached through the design-time services, produces the migration, designer and snapshot. They compile with Roslyn, `IMigrator` applies them, and `HasPendingModelChanges` is then false.
- `dotnet ef migrations add Initial` also succeeds, through an `IDesignTimeDbContextFactory`. This was run by hand; it is not part of the test suite.
- Adding a field to the schema scaffolds a single `AddColumn`, and existing rows survive it.
- `ExcludeFromMigrations` keeps `cms_document` and `cms_reference` out of the content migration. System and content streams apply to one database with their own history tables (`cms__migrations`, `cms_content__migrations`), and the union model then reads and writes all four tables.

Three traps came up:

- **Reusing a context silently produces the wrong migration.** Scaffolding on a context that already called `HasPendingModelChanges()` emits `CreateTable` for every table. A plain class-based context does the same, so this is EF behaviour and not caused by property bags. A fresh context is correct.
- **The generated migration fails this repository's build.** Its composite index columns raise CA1861 under `latest-recommended` with warnings as errors (six errors for the spike's four tables). The designer and snapshot files are marked `<auto-generated />` and are skipped; adding that header to the migration file made the build pass. This was checked by hand.
- **`Migrate()` creates `__EFMigrationsLock` on SQLite.** The table has no `cms_` prefix and all streams share it.

### Projection

Confirmed. A LINQ join over the shared, locale and document rows projects into a typed `Article`. A projection built at runtime from `EF.Property<T>` calls, which is what a reflection-based mapper would do, selects only the requested columns:

```sql
SELECT "c0"."title" AS "Title", "c"."category" AS "Category"
FROM "cms_article" AS "c"
INNER JOIN "cms_article_i18n" AS "c0" ON "c"."document_id" = "c0"."document_id" AND "c"."state" = "c0"."state"
WHERE "c0"."locale" = 'en'
```

It composes with filtering, sorting and paging. A missing locale row projects nulls through a left join. A block list deserializes into typed blocks after the query. A relation path translates to nested `EXISTS` through `cms_reference` to the target row in the same state and locale.

### GUID v7 keys

Confirmed. A `ValueGenerator` assigns `Guid.CreateVersion7()` to `id` on all four tables when the dictionary omits it. SQLite stores it as 36 characters of text. Text order matches creation order across milliseconds but not within one, and a keyset predicate (`"c"."document_id" > @cursor`) translates and pages correctly.

### SQLite column types

- **`DateTimeOffset` supports equality only.** `>` throws `InvalidOperationException` and `ORDER BY` throws `NotSupportedException`. Converting to UTC ticks stores an integer that compares and sorts correctly across offsets, but drops the offset. A `Properties<DateTimeOffset>()` convention also covers system columns such as `schedule_publish_at`.
- **`decimal` needs no conversion.** It is stored as text, and EF compares it with `ef_compare()` and sorts it with `COLLATE EF_DECIMAL`, which give numeric results.
- **`varchar(n)` is not enforced.** The column is `TEXT` with no length, so the validation pipeline is the only limit.

## Changes to the specification

These were required, and [#2722](https://github.com/fluentcms/FluentCMS/pull/2722) applied them to the specification.

| Section | Change | Why |
| --- | --- | --- |
| 4.3 Model building | Say the spike validated SQLite only and that #2711 validates the other providers. | Q19. |
| 4.3 Tables | Add the `id` v7 primary key to the `cms_article` and `cms_article_i18n` rows, and `site_id` to the `cms_article_i18n` row. | Appendix A has both and the table does not. An index cannot span tables, so a localized `[Unique]` field needs `site_id` beside it. |
| 4.3 Tables, Appendix A | Use a non-null sentinel such as `''` for non-localized values in `cms_reference`, where Appendix A says `locale NULL`, and in `cms_document`. | A null `locale` defeats the unique keys of both tables. |
| 4.3 Column mapping | Add SQLite: `dateTime` as UTC ticks in an integer column through a provider convention, including system columns; `decimal` as text; JSON as text with `CHECK (json_valid(col))`; `varchar(n)` as unbounded `TEXT`. | The mapping names only PostgreSQL and SQL Server for dates, and scheduling and `sort=-publishedOn` need comparison and ordering. |
| 4.3 Indexes | Scope `[Unique]` by table: `(site_id, locale, field)` for localized fields, `(site_id, field)` for non-localized fields, and without `site_id` for `ContentScope.Global` types. | A shared-row table has no `locale`, and a global type's `site_id` is null. |
| 4.3 JSON dialect | Make `IJsonSqlDialect` contribute EF function mappings and LINQ expressions, not SQL text, with one comparison per field type. | Filters compile to LINQ, and `json_extract` comparisons depend on the type. |
| 4.3 Migration streams | Key the model cache on the schema hash. Always scaffold on a fresh context. Mark generated migrations `<auto-generated />`. Accept `__EFMigrationsLock` as an exception to the prefix rule on SQLite. | The traps above. |
| 4.4 Compilation | Inline `state` as a literal and bind every user value as a parameter, using a member read on a holder object. | A bare expression constant becomes a SQL literal. SQLite does not need the inlined `state`, but it matches the index predicate as written and #2711 can check it on the other providers. |
| 4.4 Pagination | State that a v7 `document_id` gives a stable final sort key, not creation order within a millisecond. | Measured above. |

## Not verified at the time

The spike left these to #2711. [Verified on the server providers](#verified-on-the-server-providers) has the results.

PostgreSQL 15:

- `jsonb` operators for block presence and scalar path filters, and GIN indexes.
- Partial unique indexes, including whether a bound `state` can use them.
- Migration generation, compilation and application, including system migrations.
- `SKIP LOCKED` job claiming.
- `uuid` ordering of v7 values, and the `timestamptz` and `numeric` mappings.

SQL Server 2019 and 2025:

- `JSON_VALUE` and `OPENJSON` for the same filters.
- 2019 versus 2025 JSON behaviour.
- Filtered unique indexes, including whether a bound `state` can use them.
- Migration generation, compilation and application, including system migrations.
- `READPAST` job claiming.
- `uniqueidentifier` ordering of generated keys, and the `datetimeoffset` mapping.

Across providers:

- Whether filters are identical on all providers, as Q7 requires: type comparison, case sensitivity of containment and null handling.
- Whether the function-mapping shape of `IJsonSqlDialect` fits PostgreSQL's operators.
- Whether the other providers need a migration lock table.

Also not covered on SQLite:

- `cms_version`, the plugin migration stream and `Database:SchemaSync = Auto`.
- File databases, concurrent writers, and query performance at volume.

## Verified on the server providers

Added for #2711. `tests/FluentCMS.Data.Tests` runs on the provider that `FLUENTCMS_TEST_PROVIDER` names, with a container per run through Testcontainers and a database per test: PostgreSQL 15.19, SQL Server 2019 CU32 and SQL Server 2025 CU10. The suite passes on all three and on SQLite, in the `Data tests` jobs of CI ([run 42550863497](https://github.com/fluentcms/FluentCMS/actions/runs/42550863497)). A server run skips the 80 tests of SQLite itself, such as its journal modes and query plans.

PostgreSQL 15:

| Item | Result |
| --- | --- |
| `jsonb` filters and GIN indexes | Verified. Equality is `jsonb` containment (`@>`), which keeps the JSON type, and block presence reads `jsonb_array_elements`. An `[Indexed]` JSON field gets a GIN index, which the PostgreSQL migration script carries (`Providers/OfflineProviderTests`); whether the planner uses it was not measured. |
| Partial unique indexes | Enforced, races included. The compiler inlines `state` (spec 4.4), so a query names the predicate of the index as written. Plans were not read on PostgreSQL. |
| Migrations | Verified. Every test database is built by the system migrations, and the content migrations are scaffolded, compiled and applied for PostgreSQL. |
| `SKIP LOCKED` claiming | Verified by the job runner, failure and lease tests, two runners included. |
| `uuid`, `timestamptz` and `numeric` | `uuid` compares bytewise, so v7 values sort by time across milliseconds, and keyset paging passes. `timestamptz` keeps microseconds, which led to the second decision below. `numeric` compares and sorts as the filter and sort tests expect. |

SQL Server 2019 and 2025:

| Item | Result |
| --- | --- |
| `JSON_VALUE` and `OPENJSON` | Verified on both. A whole number compares through `TRY_CONVERT(decimal(38,18), ...)`, because `TRY_CONVERT(bigint, '12.0')` is null. |
| 2019 versus 2025 JSON | `nvarchar(max)` on both; see [SQL Server JSON](#sql-server-json). |
| Filtered unique indexes | Enforced on both, with `IS NOT NULL` for a nullable column (spec 4.3). Plans were not read. |
| Migrations | Verified on both. One SQL Server set serves both versions. |
| `READPAST` claiming | Verified. Under `UPDLOCK, READPAST` the claim is an ordered seek of `ix_cms_job_claim` with no sort, so a claimer stops at the first row it can lock (`Jobs/JobClaimPlanTests`). It keeps both hints with `READ_COMMITTED_SNAPSHOT` on. |
| Reads | `READ_COMMITTED_SNAPSHOT` is on, so reads take no locks, as on PostgreSQL. The system migrations turn it on ([ADR 0005](0005-sql-server-read-committed-snapshot.md)). |
| `uniqueidentifier` and `datetimeoffset` | Generated keys are sequential ([SQL Server keys](#sql-server-keys)), and the paging tests take the provider's own id order. `datetimeoffset` is written in UTC and to the microsecond. |

Across providers:

| Item | Result |
| --- | --- |
| Identical filters | Verified: the filter and JSON filter tests pass unchanged on all four. Content text columns have a binary collation on SQL Server (spec 4.3), and a negated JSON filter keeps rows that lack the path on PostgreSQL as on SQLite. |
| `IJsonSqlDialect` on PostgreSQL | It fits: the PostgreSQL dialect maps `jsonb` functions and containment through it. |
| A migration lock table | Not needed. SQL Server takes an application lock (`sp_getapplock` on `__EFMigrationsLock`) and PostgreSQL locks the stream's history table, so `__EFMigrationsLock` is SQLite's alone. Four nodes that migrate at once record one schema history row on every provider. |

On SQLite, the real code covers what the spike did not: `cms_version`, the plugin streams and schema sync (`Content/VersionDiffTests`, the plugin test projects, `Migrations/SchemaSyncTests`), and file databases and concurrent writers (`Sqlite/JournalModeTests`, `Sqlite/WriteContentionTests`).

Still not verified:

- Query performance at volume on any provider, and what the v7 ids that the code sets cost SQL Server's indexes.
- The PostgreSQL and SQL Server migration sets of the webhooks, media and sample plugins in CI. Offline tests check them against their models and the SQLite sets, and the plugin tests run on SQLite. The webhooks and media sets were applied by hand, through the reference host, to PostgreSQL 15.19 and SQL Server 2025 CU10.

### Decisions from the provider runs

- **`jsonb` stays, and reads come back in write order.** PostgreSQL rewrites `jsonb` text: spaces after `:` and `,`, and keys in its own order. Rich text now compares as JSON (`EntryJson.SameDocument`). A provider whose JSON reads back as other text (`IStorageConventions.ReordersJson`, true on PostgreSQL) has each value put back in the form a write gives it (`StoredOrder`): a block's `type` and `id` first and then its fields in schema order, a reference as `id` and `type`, and rich text as `RichTextSerializer` writes it. A `json` field keeps the order it is read in.
- **A `DateTimeOffset` is kept to the microsecond on every provider.** `timestamptz` keeps microseconds, so a value did not round-trip to the tick on PostgreSQL. `StorageConventions.Configure`, which every context applies, cuts each `DateTimeOffset` to the microsecond in front of the provider's own converter, on SQLite and SQL Server as well, so a value reads back the same everywhere. Values written with raw SQL, such as leases, go through the same cut (`StorageConventions.ToMicroseconds`).
- **A lease has ended once `lease_until <= now`.** Npgsql cuts a parameter to the microsecond, so with `<` a lease stayed taken one tick after it ended. The job queue, the outbox, the distributed lock and the three claim dialects use `<=`.
- **SQL Server has no native `json` in 1.0**, as [SQL Server JSON](#sql-server-json) records.
- **SQL Server reads row versions.** Its locking reads deadlocked with writes, so the system migrations turn `READ_COMMITTED_SNAPSHOT` on, as [ADR 0005](0005-sql-server-read-committed-snapshot.md) records.

## SQL Server keys

Added for [#2879](https://github.com/fluentcms/FluentCMS/issues/2879) of the review of 7 Nov 2026 ([#2847](https://github.com/fluentcms/FluentCMS/issues/2847)), without a database.

SQL Server sorts `uniqueidentifier` by its last six bytes first, and those are random in a v7 value, so v7 keys would land all over a clustered primary key and an `ORDER BY id` tiebreak would not follow creation. Generated keys therefore come from `IStorageConventions.ConfigureKey`. SQLite and PostgreSQL keep `Guid.CreateVersion7()`, and SQL Server uses EF Core's `SequentialGuidValueGenerator`, which rises within a process. A value generator is not in the model snapshot, so no migration changes.

Before the switch, the one place in .NET that ordered generated ids by value, the rows of an entry in `EntryStore.DocumentsAsync`, moved into the query and onto `created_at`. Keyset cursors compare `document_id` in SQL. `document_id` and the other ids that the code sets itself stay v7 on every provider. #2711 ran the suite on SQL Server with them, but did not measure what they cost its indexes.

## SQL Server JSON

Added for [#2711](https://github.com/fluentcms/FluentCMS/issues/2711), after the provider matrix ran the data tests on SQL Server 2019 and 2025.

SQL Server stores JSON as `nvarchar(max)` with a `CHECK (ISJSON(col) = 1)` constraint on every version, 2025 included, and filters go through `OPENJSON`, which reads the text on both. 1.0 has no option for the native `json` type of SQL Server 2025: `UseSqlServer(nativeJson: true)` is removed. The option mapped the JSON columns as `json` while the one SQL Server system migration set creates `nvarchar(max)`, so every migrated 2025 database stopped with `PendingModelChangesWarning`, and nearly all of the 1126 failures of the first 2025 run came from that. A second system migration set, and a second set for every content and plugin stream, would cost more than the type gives a 1.0 that filters one level deep.

Native `json` can return later as an opt-in with migration sets of its own, and a way to move an existing database between the two forms.

## Where the evidence lives now

The spike project was a throwaway. #2736 deleted it after the real provider tests in `tests/FluentCMS.Data.Tests` covered what it proved, and moved the rest there, rewritten against the real code. The spike's tests and the SQL they asserted stay in the git history before that change. Everything above is a record of what was run, not of what is in the tree.

| Finding | Tests now |
| --- | --- |
| Property bags: round trip, omitted columns, one-column update, per-schema model, a model of 50 types | `Storage/ContentRowTests`, `Storage/ContentModelTests`, `Storage/ModelCacheTests`, `Storage/LargeModelTests` |
| JSON filters: block presence, scalar paths, containment, type-aware comparison, one-level paths, malformed JSON | `Query/JsonFilterTests`, `Sqlite/ConventionsTests`, `Query/FilterTests`, `Query/CompiledSqlTests` |
| JSON block filter is a full scan | `Storage/QueryPlanTests` |
| Partial unique indexes: enforced, scoped by site and locale, global types, empty locale | `Storage/ContentRowTests`, `Storage/ContentModelTests`, `Storage/SystemModelTests` |
| Query plans of the partial index and of the reference target index | `Storage/QueryPlanTests` |
| Migration generation: auto-generated header, fresh context, `AddColumn` keeping rows, system and content streams, `__EFMigrationsLock`, the CLI path | `Migrations/ContentMigrationGeneratorTests`, `Migrations/MigrationRunnerTests`, `Migrations/MigrationsAddCommandTests`, `Migrations/SchemaSyncTests`, `Sqlite/SystemMigrationTests` |
| Projection: only the requested columns, relation paths, paging and sorting | `Query/CompiledSqlTests`, `Query/FilterTests`, `Query/PaginationTests`, `Query/PopulationTests` |
| GUID v7 keys: generated, stored as 36 characters of text, text order across milliseconds, keyset paging | `Storage/SystemModelTests`, `Storage/ContentRowTests`, `Query/PaginationTests` |
| SQLite column types: dates compare and sort across offsets, decimals compare and sort numerically, `varchar(n)` is not enforced | `Sqlite/ConventionsTests`, `Query/FilterTests`, `Query/PaginationTests`, `Storage/ContentRowTests` |
| Bundled SQLite is at least 3.45 | `Sqlite/HostTests` |

Not carried over, because the real code no longer depends on them: that nulls are distinct in a unique index (the design answers it with `''` and a global scope, both tested), that a v7 key is not monotonic within one millisecond (the sort key falls back to `document_id`, tested in `Query/PaginationTests`), and that a plain `DateTimeOffset` cannot be compared (the UTC ticks convention is tested instead). The hand-run timings and the `dotnet ef` run in the records above were never tests.

## Consequences

Attribute-defined and builder-defined types share one code path, and a user's class stays a plain projection. In exchange, column names are strings until the query compiler and mapper are written, and those are built from expression trees, so mistakes surface when a query runs and not at compile time.

Follow-up work:

- [x] Apply the specification changes above. Done in [#2722](https://github.com/fluentcms/FluentCMS/pull/2722) for [#2721](https://github.com/fluentcms/FluentCMS/issues/2721).
- [x] Build on them in [#2688](https://github.com/fluentcms/FluentCMS/issues/2688) (model, [#2729](https://github.com/fluentcms/FluentCMS/pull/2729)), [#2689](https://github.com/fluentcms/FluentCMS/issues/2689) (JSON dialect and SQLite conventions, [#2737](https://github.com/fluentcms/FluentCMS/pull/2737)), [#2690](https://github.com/fluentcms/FluentCMS/issues/2690) (migration streams, [#2739](https://github.com/fluentcms/FluentCMS/pull/2739)) and [#2692](https://github.com/fluentcms/FluentCMS/issues/2692) (query compiler, [#2740](https://github.com/fluentcms/FluentCMS/pull/2740)).
- [x] Delete `tests/FluentCMS.Storage.Spike.Tests` once #2688 lands. Done in [#2736](https://github.com/fluentcms/FluentCMS/issues/2736).
- [x] Run the [Not verified](#not-verified-at-the-time) list in #2711 and update this record with the results. Done in [Verified on the server providers](#verified-on-the-server-providers).
