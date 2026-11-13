# FluentCMS.Plugins.Search

Full-text search for `q=` on the Delivery and Management lists. The plugin fills the `ISearchIndex` provider slot with an index of the fields marked `[Searchable]`, keeps it current from the entry events and rebuilds it on request. Section 5.7 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

The plugin is named `search`. It has no options.

```csharp
builder.Services.AddFluentCms(cms =>
{
    cms.UsePostgreSql(builder.Configuration.GetConnectionString("cms"));
    cms.AddPlugin<SearchPlugin>();
});
```

## What a search finds

- The text of the fields of a content type marked `[Searchable]` (`"searchable": true` in a schema file), at the top level of the type and not `[Hidden]`: text, the texts of a list such as a multiple select, and rich text as plain text. Numbers, dates, relations, media, components and blocks have no text to search.
- Every word of the search must match, and the last one matches as the start of a word, so a search box finds while the editor types: `release no` finds "Release notes". Case and accents do not count: `cafe` finds "Café". On SQL Server every word matches as a part of the text, so `otnet` finds "dotnet" there and nowhere else.
- The index finds ids, and the list keeps its filter, row rule, site, locale, status, sort and page, so a search never shows an entry the caller could not list. A draft is found with `status=draft` and on Delivery only once it is published.
- The index gives at most `FluentCms:Query:MaxSearchResults` ids (1,000), the most recently changed first when more match. There is no ranking by relevance and no stemming or stop words for a language.
- A type without such a field refuses `q=` with `FCMS1700` (400), and a site where the plugin is disabled with `FCMS2211` (400), as a host without an index does.
- Any caller who may list the type can learn whether an entry holds a word of a searchable field, even a caller who may not read that field. Mark only fields whose text every reader of the type may know.

The text is folded the same way for every database: lower case, the accents taken off, and words of letters and digits, so punctuation separates words. One entry keeps at most 100,000 characters of it.

| Database | Index |
| --- | --- |
| SQLite | an FTS5 table, `cms_search_entry_fts`, over the text of `cms_search_entry`, which triggers keep in step |
| PostgreSQL | a GIN index over `to_tsvector('simple', text)` |
| SQL Server | none: `LIKE '%word%'` over the text, which reads every row of the type, locale and state. A full-text catalogue is not used, so the plugin runs on every edition and on Azure SQL; with a large index, search on PostgreSQL or SQLite is faster |

The tables are the stream `cms_search__migrations`, one migration, `InitialSearch`, in a set per provider. The FTS5 table and the GIN index are written by hand into the SQLite and PostgreSQL migrations.

## Keeping the index current

The outbox handlers of the entry events (a draft saved, published, unpublished, a draft discarded, a version restored, trashed, restored and purged) read the entry again through the content service and write its rows, one for each locale and state. So a change is found once its events are delivered, a moment after the write, and a delivery that fails is tried again by the outbox. An import and a seed raise the same events. A trashed entry keeps its draft row, so `q=` works on the trash list too; a purge removes its rows. The rows of a site go with the site.

Core evicts the Delivery copies of the type through the outbox too, and the plugin's handlers start first. When a Delivery search runs between that eviction and the plugin's delivery, as after a delivery of the plugin that failed and runs later, the output cache can serve the older answer until the type changes again or `OutputCacheDuration` (60 seconds) ends.

## Rebuild

```http
POST /api/management/v1/search/rebuild
Content-Type: application/json

{ "type": "article" }
```

needs `search.manage` in the site of the request, answers `202` with `{ "jobId" }` and queues the job `search.rebuild` for that site, which `fluentcms jobs list` shows. Without a body, or without `type`, it rebuilds every type with a searchable field. The job reads the entries of the site and of the global types, the drafts, the published versions and the drafts in the trash, a page at a time, writes what differs and removes the rows of entries that no longer exist. A second run writes nothing, and an attempt that fails starts again. A type that is unknown is `FCMS1701` (422), and one without a searchable field `FCMS1700`.

Run it once:

- when you add the plugin to a host that already has content;
- after a schema change that marks a field `[Searchable]`, or no longer, since the rows change only as entries change.

A list reads one locale, so the job lists a localized type in the site's default locale, in every locale that the entries in it hold, and in every locale the index holds. An entry that exists only in a locale that none of those has is not found by the rebuild, and is indexed at its next change.

## Per site

Where the plugin is disabled in a site, `q=` is refused there, and its handlers hear nothing of the site (4.13). Enabling it again queues a rebuild of the site (event 9382). The rows of a global type are shared by every site, so a change to a global entry made in a site where the plugin is disabled reaches them only with a rebuild.

## Codes and log events

| Code | Status | Meaning |
| --- | --- | --- |
| `FCMS1700` | 400 | the type has no field marked `[Searchable]` |
| `FCMS1701` | 422 | a rebuild that names no content type |

`FCMS1702` to `FCMS1719` are free. Log event ids: 9380 a rebuild with what it wrote and removed, 9381 the rows of a deleted site, 9382 a rebuild queued when the plugin is enabled in a site and 9383 an entry that could not be read for the index, of 9380 to 9399. No line holds the text of an entry.
