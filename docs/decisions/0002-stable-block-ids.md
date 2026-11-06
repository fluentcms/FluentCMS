# 0002 Give blocks stable ids assigned by the service

## Status

Accepted.

## Date

2026-11-06

## Context

Blocks and inline blocks had no identity. On an update or a patch, the content service paired each block it was sent with a stored block. It used identical visible content first, and then order within the block's type. Hidden values were carried along those pairs, and read-only values sent back were checked against them (`FCMS1406`, spec 4.5). So a write that removed, moved, edited or duplicated blocks could put a value on the wrong block, or refuse a correct write. Take two hero blocks, A approved and B rejected. Delete A and rename B, and B was saved as approved. Pairing also stopped at the edge of a list or a document. So a block moved into or out of the document of an inline block took another block's values. [#2751](https://github.com/fluentcms/FluentCMS/issues/2751) raised this from [#2747](https://github.com/fluentcms/FluentCMS/issues/2747), and [#2775](https://github.com/fluentcms/FluentCMS/issues/2775) found the case of identical blocks.

Ids change the stored format in Appendix A, so this had to be settled before v1. Three options were considered:

- **A. Service-assigned ids.** The service gives each block an id and returns it on reads. A write pairs a block by id. A block without an id is paired as before.
- **B. Client-supplied ids.** The same key, but every write must send an id on every block. This breaks every current writer, including typed `IContentService<T>`, whose block classes have no id.
- **C. No ids.** Refuse a write when a block with a hidden or read-only value cannot be paired unambiguously. This refuses ordinary edits, and identical blocks stay ambiguous. The refusal also tells the client that a hidden value exists, which 4.5 forbids.

## Decision

We will adopt option A:

- **Key.** A block carries `id` beside `type`, and an inline block carries `attrs.id`. `id` is a reserved block field name (`FCMS1206`), as `type` is.
- **Assignment.** The service gives a block without an id a 12-character random string of URL-safe characters, made with `RandomNumberGenerator`. It stores the id and returns it on reads. A write may send any id of 1 to 64 URL-safe characters (`A-Z`, `a-z`, `0-9`, `-`, `_`), so imported ids survive.
- **Scope.** An id is unique within one top-level field, at every depth. An id repeated in one write is refused (`FCMS1407`).
- **Pairing.** A block sent with an id pairs with the stored block of the same id and type, anywhere in the same top-level field. A block sent without an id is paired as before, within its list or document, among the stored blocks whose ids the write does not send. It then takes the id of its pair, or a new one.
- **Unknown id.** A block whose id is not stored is a new block, not a refusal. It keeps its id and carries no hidden value, and a read-only value sent on it is refused as before. Copying between entries or locales and importing keep working.
- **Typed blocks.** A block class may declare an optional `string? Id` property with no field attribute, to keep its id through `IContentService<T>`. A class without it uses the pairing without ids.
- **Migration.** None, since nothing has shipped. Stored blocks get ids on their next write, and old versions restore through the pairing without ids.

Repeated components keep the pairing without ids for now.

## Consequences

- Moving, editing, removing and copying blocks in one write keeps each hidden and read-only value with its own block, as long as the client sends the ids back. This also holds across nested lists and inline block documents.
- Front ends get stable keys for block lists.
- A client that drops ids still gets the guess. For example, a new block without an id, written in the same write that removes a block of its type, pairs with the removed block. A client that wants a block treated as new can send an id of its own.
- The admin must drop the id when it duplicates or pastes a block, or the write is refused as a duplicate.
- Every block in a response is longer by its id. Schema introspection is unchanged, as it is for `type`. The OpenAPI block schemas gain an optional `id`.
- The read-only check and the carry of hidden values both pair through one index of the field's stored blocks, so the two still cannot disagree.
- Follow-up: repeated components could get ids the same way if their pairing turns out to need it.
