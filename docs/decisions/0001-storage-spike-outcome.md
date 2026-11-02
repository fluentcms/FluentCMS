# 0001 Use property bags for content tables

## Status

Accepted. Confirmed on SQLite only. PostgreSQL and SQL Server stay unverified until [#2711](https://github.com/fluentcms/FluentCMS/issues/2711); see [Not verified](#not-verified).

## Date

2026-11-02

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

These are required, and are not yet applied to the specification.

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

## Not verified

All of these are deferred to #2711, and any of them could change this record.

PostgreSQL 15:

- `jsonb` operators for block presence and scalar path filters, and GIN indexes.
- Partial unique indexes, including whether a bound `state` can use them.
- Migration generation, compilation and application, including system migrations.
- `SKIP LOCKED` job claiming.
- `uuid` ordering of v7 values, and the `timestamptz` and `numeric` mappings.

SQL Server 2019 and 2025:

- `JSON_VALUE` and `OPENJSON` for the same filters.
- 2019 versus 2025 JSON behaviour, including `nvarchar(max)` versus the `json` type.
- Filtered unique indexes, including whether a bound `state` can use them.
- Migration generation, compilation and application, including system migrations.
- `READPAST` job claiming.
- `uniqueidentifier` ordering of v7 values, and the `datetimeoffset` mapping.

Across providers:

- Whether filters are identical on all providers, as Q7 requires: type comparison, case sensitivity of containment and null handling.
- Whether the function-mapping shape of `IJsonSqlDialect` fits PostgreSQL's operators.
- Whether the other providers need a migration lock table.

Also not covered on SQLite:

- `cms_version`, the plugin migration stream and `Database:SchemaSync = Auto`.
- File databases, concurrent writers, and query performance at volume.

## Where the evidence lives now

The spike project was a throwaway. #2736 deleted it after the real provider tests in `tests/FluentCMS.Data.Tests` covered what it proved, and moved the rest there, rewritten against the real code. The spike's tests and the SQL they asserted stay in the git history before that change. Everything above is a record of what was run, not of what is in the tree.

| Finding | Tests now |
| --- | --- |
| Property bags: round trip, omitted columns, one-column update, per-schema model, build time | `Storage/ContentRowTests`, `Storage/ContentModelTests`, `Storage/ModelCacheTests`, `Storage/ModelBuildTimeTests` |
| JSON filters: block presence, scalar paths, containment, type-aware comparison, one-level paths, malformed JSON | `Sqlite/JsonFilterTests`, `Sqlite/ConventionsTests`, `Query/FilterTests`, `Query/CompiledSqlTests` |
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

- [ ] Apply the specification changes above.
- [ ] Build on them in [#2688](https://github.com/fluentcms/FluentCMS/issues/2688) (model), [#2689](https://github.com/fluentcms/FluentCMS/issues/2689) (JSON dialect and SQLite conventions), [#2690](https://github.com/fluentcms/FluentCMS/issues/2690) (migration streams) and [#2692](https://github.com/fluentcms/FluentCMS/issues/2692) (query compiler).
- [x] Delete `tests/FluentCMS.Storage.Spike.Tests` once #2688 lands. Done in [#2736](https://github.com/fluentcms/FluentCMS/issues/2736).
- [ ] Run the [Not verified](#not-verified) list in #2711 and update this record with the results.
