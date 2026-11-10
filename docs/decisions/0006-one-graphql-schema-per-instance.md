# 0006 One GraphQL schema per instance, with permissions enforced at execution

## Status

Accepted.

## Date

2026-11-10

## Context

The GraphQL plugin builds its schema from the content schema ([#2982](https://github.com/fluentcms/FluentCMS/issues/2982), section 5.3 of the [backend technical specification](../planning/backend-technical-spec.md)). The REST schema routes show a caller only what it may read: `/schema` leaves out a type or a field the caller may not read, so that no name of such a type shows (4.10). A GraphQL schema is a document that every query is validated against, and a client fetches it by introspection. The question is whether it follows the caller's permissions too.

Options considered:

- **A. One schema per instance.** The schema holds every content type and field that is not hidden. What a caller may read or write is decided when a query runs, by the content service, as for REST.
- **B. A schema per permission set.** Each distinct set of grants gets a schema with only the types and fields it may read. A query that names anything else fails validation as an unknown field.

Option B hides names, but:

- A grant can be limited to a site, a locale, a field list and a row rule, and a principal's set is the union of its roles' grants. The number of distinct schemas has no bound, each costs a HotChocolate schema and executor in memory, and each must be rebuilt when a role changes. A cache of them would be a new thing to evict on every instance.
- A persisted query, a generated client and a cached document would be valid for one caller and not for another, and would stop being valid when a role changes.
- A field a caller may not read would be a validation error for the whole request, where REST answers the rest of the entry.

## Decision

We will build one schema per instance when the host starts, and enforce permissions when a query runs: the plugin asks `IContentService` for content and `IContentSurface` for what the principal may read, and a type or a field the principal may not read is null with an error that carries the code REST gives it (`FCMS3104` for a type, `FCMS2202` for a field).

Introspection therefore shows every type and field name of the schema, whatever the caller may read. Outside Development it needs a signed-in caller that holds `schema.read` in the site of the request, and it is open in Development. The schema download and the IDE of HotChocolate are not mapped.

## Consequences

- A caller with `schema.read` sees through GraphQL introspection the names of types and fields that `/schema` would hide from it. It sees names, argument and input shapes, and no content. An operator who needs those names hidden from a role does not grant it `schema.read`, or turns the plugin off for the site.
- A caller without `schema.read` can still learn whether a name exists: a query that names an unknown field fails validation, and one that names a field it may not read answers null with an error. REST hides this on its content routes by answering both as an unknown field. Hidden fields are in no GraphQL type, so they stay unknown to everyone.
- The schema is built once, and a role change takes effect on the next request with nothing to rebuild or evict.
- One document is valid for every caller, so persisted queries and generated clients do not depend on who runs them.
- A response can be partial: the fields the principal may read are answered beside the errors of those it may not.
- A schema per permission set stays possible later for introspection alone, by filtering the introspection result per caller as `/schema` does, without changing execution.
