# Delivery API

The delivery API serves published content to frontends and agents over HTTP (API-01). It is read-only and anonymous: anyone who can reach the host can read what is published, and nothing else.

## What it serves

Only the published version of each entry is served, and only while the entry is not deleted. Drafts, unpublished entries, archived entries and deleted entries answer 404, the same as an id that never existed.

Each item is a resolved view (CNT-04):

- A reference to an entity field holds that entity's current published value. A newer draft of the entity changes nothing until it is published.
- A reference that has no published value is `null`, and `unresolved` says why: `notPublished`, `noValue` or `deleted`.
- References to whole entries and to media stay ids. Read a referenced entry at `/api/v1/entries/{id}`.

Entities are content too, so their fields hold facts as stored: each value with its source, owner and validity.

## Endpoints

| Request | Returns |
| --- | --- |
| `GET /api/v1/content/{type}` | The published entries of a content type, in the order they were created. |
| `GET /api/v1/entries/{id}` | One published entry. |
| `GET /api/v1/entries/{id}.md` | The same entry as Markdown. |
| `GET /api/v1/index` | Every published entry with its type, title and URLs, as JSON. |
| `GET /api/v1/index.md` and `GET /llms.txt` | The same index as Markdown, grouped by content type. |

`GET /api/v1/content/{type}` takes these query parameters:

| Parameter | Meaning |
| --- | --- |
| `limit` | Items per page, from 1 to 100. Defaults to 20. |
| `after` | The `next` cursor of the previous page. |
| `references` | Keeps the entries whose published version points at this entry, entity or media asset, such as every product page that shows one product. |

A list looks like this:

```json
{
  "items": [
    {
      "id": "0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e",
      "type": "product-page",
      "version": 3,
      "fields": { "product": "0192f3a1-0000-7000-8000-000000000001", "price": 99, "headline": "A sturdy drill" },
      "markdown": "/api/v1/entries/0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e.md",
      "unresolved": []
    }
  ],
  "next": "0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e"
}
```

`next` is `null` on the last page. Pages are keyed by id, so entries added while a client pages through do not shift the pages it has not read yet.

## Markdown and the site index

Each published entry has a Markdown representation at `/api/v1/entries/{id}.md` (API-03). The URL is the entry's id, so it stays the same across versions and always shows the published version.

- Front matter gives the id, type, version and URL, as quoted strings apart from the version number.
- The heading is the entry's title: its first text field that holds text.
- Each other field follows under its name, in the order of the content type definition that the published version was written against:
  - rich text as written, since it is stored as Markdown;
  - other text escaped so it reads as written, which means a value can never pose as a heading, a field, a link or HTML;
  - lists as bullets, each item keeping its own paragraphs, and JSON in a fenced block;
  - references to whole entries as links to their Markdown.
- On an entity, each fact's value is followed by a `**Source:**` item, which also says when the fact is unsourced.
- References that could not be resolved are listed under "Unresolved references", each with its reason.

The site index lists every published entry for agents. `/llms.txt` serves it where agents look for it first.

## Preview

A preview token lets a frontend show a change set's content resolved as it would be published, before anyone approves it (API-04). A person issues one for a set; agents cannot. Send it in the `Authorization` header, never in the URL:

```http
GET /api/v1/preview/entries/0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e
Authorization: Bearer fcmsp_...
```

| Request | Returns |
| --- | --- |
| `GET /api/v1/preview/entries/{id}` | The entry as the set would publish it: the version the set proposes, or the published version when the set leaves the entry alone. An entry the set unpublishes is a 404. |
| `GET /api/v1/preview/changes` | `changeSet` and the `items` the set creates or updates, in the set's order. |

References to entity fields resolve to the values the set proposes, so a page previews the new price of its product.

A token stops working when it is revoked or expires (`Delivery__PreviewTokenLifetime`, 7 days by default), and when its set is published, rejected or reverted. Every one of these is the same 401 with `preview_token_invalid`. Preview responses carry `Cache-Control: no-store`.

## Errors

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details (`application/problem+json`). `errors` lists each error with a stable `code`:

| Status | Codes |
| --- | --- |
| 400 | `limit_invalid`, `parameter_invalid` |
| 401 | `preview_token_invalid` |
| 404 | `entry_not_found`, `content_type_not_found`, `endpoint_not_found` |
