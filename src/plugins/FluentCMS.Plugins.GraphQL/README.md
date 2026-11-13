# FluentCMS.Plugins.GraphQL

GraphQL as a second surface over the services of the REST APIs: one endpoint, `/graphql`, whose schema is built from the content schema when the host starts. Every read and write goes through `IContentService`, so permissions, row rules, field limits, validation, versions, events and the activity log are those of REST. Section 5.3 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design, and [ADR 0006](../../../docs/decisions/0006-one-graphql-schema-per-instance.md) the choice of one schema per instance.

A host adds the plugin with `cms.AddPlugin<GraphQLPlugin>()`, or by referencing the package, which is discovered. It builds on [HotChocolate](https://chillicream.com/docs/hotchocolate) 16, and its schema is named `FluentCMS`, so a host with a HotChocolate schema of its own keeps the default one.

## Settings

The options are in `FluentCms:GraphQL`:

| Key | Default | Meaning |
| --- | --- | --- |
| `MaxDepth` | `12` | The deepest a query may nest its fields. Introspection fields are not counted. |
| `MaxFieldCost` | `1000` | The most a query may cost by HotChocolate's cost analysis. A root field costs 10 and a populated relation or media field 1 for each entry of the page above it. |
| `MaxTypeCost` | `1000` | The most objects a query may return, by the same analysis. |
| `MaxRequestSize` | `262144` | The body of a request, in bytes. `FluentCms:Limits:MaxBodySize` still caps it. |
| `ExecutionTimeout` | `00:00:30` | How long a request may run. |
| `OpenIntrospection` | unset | Whether any caller may introspect the schema. Unset, it is open in Development, and elsewhere needs a signed-in caller with `schema.read`. |
| `PersistedQueries` | unset | A folder of query files, each named `{id}.graphql`. A relative path is under the content root. |
| `OnlyPersisted` | `false` | Refuses every query that is not in the folder. |

The page size and the populate depth are those of REST, in `FluentCms:Query`. Options that are not valid, a folder that does not exist and two content types that would share a GraphQL name stop the host (`FCMS1640`, `FCMS1641`).

## The schema

| Content schema | GraphQL |
| --- | --- |
| Content type `site-settings` | Object type `SiteSettings`, with `id`, `status`, `locale`, `version`, `createdAt`, `updatedAt` and `publishedAt` beside its fields |
| Component `seo` | Object type `SeoComponent` |
| Block `hero` | Object type `HeroBlock` |
| Blocks field `sections` of `article` | A list of the union `ArticleSectionsBlock` |
| Select field `category` of `article` | Enum `ArticleCategory`, each option as an upper-case value (`release-notes` is `RELEASE_NOTES`) |
| Relation | The target's object type, or a list of it |
| Rich text | The scalar `JSON`: the document, or HTML or Markdown text with `richText: HTML` or `MARKDOWN` on the root field |
| Media, and any other field type a plugin stores as a reference | The scalar `JSON`: what the Delivery API answers for the asset, populated when the field is selected |
| `json` | The scalar `JSON` |
| Numbers, dates and text | `Long`, `Decimal`, `Boolean`, `DateTime`, `Date`, `ID` and `String`, by the JSON schema the field type gives the OpenAPI document |

A hidden field is in no type, and a read-only field in no input. A select whose options cannot be enum values is a `String`. A relation inside a block or a repeated component is an `EntryReference`, its `id` and `type`, since those are stored whole and never populated. Reverse relations are metadata only, as in REST, and have no field.

## Queries

A collection has a list field and a single field, and a singleton one field:

```graphql
{
  articleList(
    filter: { category: { eq: NEWS }, author: { where: { name: { contains: "ada" } } } }
    sort: [{ field: PUBLISHED_ON, direction: DESC }]
    pageSize: 10
    locale: "fr"
  ) {
    total
    nextCursor
    items { id title author { name avatar } sections { ... on HeroBlock { heading image } } }
  }
  article(slug: "welcome-to-fluentcms") { title body }
  siteSettings { tagline }
}
```

- **Arguments.** `locale`, `status` (`PUBLISHED`, the default, or `DRAFT`), `fallback` (`CHAIN` or `NONE`) and `richText` are those of REST. A field reads one locale; read another under an alias. Without `locale` it is the locale of the request, from `X-Cms-Locale` or the site.
- **Filters** mirror the REST operators (`eq ne in nin lt lte gt gte between contains startsWith null notNull`, and `hasBlock` on a block list) with `and`, `or` and `not`, and build the AST the bracket parser builds. A single component nests its fields (`seo: { metaTitle: { startsWith: "T" } }` is `filter[seo.metaTitle][startsWith]=T`), and a relation takes the id of its target (`eq`, `in`) or the target's own fields under `where` (`filter[author.name]`). A field named `and`, `or` or `not` cannot be filtered here.
- **Sort** takes the sortable fields of the type and `ID`, `CREATED_AT`, `UPDATED_AT` and `PUBLISHED_AT`.
- **Pagination** is REST's: `page` or `offset` with `pageSize`, or `cursor` with the `nextCursor` of the page before. `total` runs its count only when it is selected. A page over `FluentCms:Query:MaxPageSize` is refused with `FCMS2207`, as in REST.
- **Single entries** are read by `id` or by one unique field, such as `slug`. An entry that is not there is null.
- **Population** follows the selection: a relation whose fields are selected is populated with those fields, a level at a time, and a media field, a block list and a repeated component with the references inside them. A selection deeper than `FluentCms:Query:MaxPopulateDepth` is refused with `FCMS2207` before anything runs. A relation selected for its `id` alone costs no read.

## Mutations

Each content type has `create`, `update`, `delete`, `publish` and `unpublish`:

```graphql
mutation {
  createArticle(input: { title: "Hello", slug: "hello", author: "0192f2a4-...", category: NEWS, sections: [{ type: "quote", text: "Hi" }] }) { id version }
  updateArticle(id: "0192f2a4-...", expectedVersion: 1, input: { title: "Hello again" }) { version }
  publishArticle(id: "0192f2a4-...") { entry { status } warnings { code path message } }
}
```

- `update` is a merge patch, as `PATCH` on the Management API: a field the input leaves out keeps its value and `null` clears it. `expectedVersion` is required, and `0` adds a locale the entry does not have. A stale version is refused with `FCMS3105`, with the current entry in `extensions.current`.
- A relation and a media field are written as ids, and a block list as JSON, each block an object with its `type`, since a GraphQL input cannot be a union.
- `delete` moves the entry to the trash and answers its id. `locale` names the locale of a write.
- A write answers the entry it wrote. A selection that goes into a relation or a media field reads it again, as a draft, with that population; one deeper than `MaxPopulateDepth` is refused with `FCMS2207` before anything is written. If that read fails after the write, for instance over `FluentCms:Query:MaxPopulatedEntries`, the write stands: the entry is answered, and each field the read was to populate is null with the error at its path.
- Scheduling, versions, bulk writes, the trash and discarding a draft stay with the Management API. Subscriptions and uploads are not served.

## Who may do what

The request is authenticated as the REST APIs authenticate it, with a bearer token or an API key, and an anonymous caller is the Public role of its site. The content service decides the rest:

- A Delivery key or an anonymous caller reads published entries only (`FCMS2209` for a draft) and is refused every mutation (`FCMS3103`, or `FCMS3102` when not signed in).
- A Preview key or the `draft` scope reads drafts. A principal with the `management` scope runs the mutations its roles allow.
- A media field, and an image of rich text, show to a caller without the `management` scope (an anonymous caller, a Delivery or Preview key, a preview token) a public asset of the site's library, as the Delivery API shows it, and never a private one. A caller with the scope sees the assets its `media.read` reaches, as on the Management API. Where the media plugin is disabled for the site, no asset shows.
- A type the principal may not read is null with `FCMS3104`, and a field it may not read is null with `FCMS2202`, each reported once in a response. The rest of the response is answered.

The endpoint takes the Management rate limit for a caller with the `management` scope and the Delivery rate limit for any other, in buckets of its own, and is never in the output cache. Where the plugin is disabled for a site, `/graphql` answers 404 (`FCMS1550`).

## Errors

A refusal of the content service is a GraphQL error at the path of its field, with what REST answers in `extensions`:

```json
{
  "message": "The page size must be between 1 and 100.",
  "path": ["articleList"],
  "extensions": { "code": "FCMS2207", "status": 400, "errors": [{ "path": "pageSize", "code": "FCMS2207", "message": "..." }] }
}
```

`errors` holds the validation errors with the path of each field, and `current` the current entry of a version conflict. No response carries a stack trace; an unexpected failure is `FCMS3199` and is logged (event id 9321).

| Code | Meaning |
| --- | --- |
| `FCMS1640` | Options that are not valid, or a persisted queries folder that does not exist (stops the host) |
| `FCMS1641` | Two names of the content schema give one GraphQL name (stops the host) |
| `FCMS1642` | A single entry named by no argument, or by more than one |
| `FCMS1643` | Introspection is not allowed to the caller |
| `FCMS1644` | The query nests deeper than `MaxDepth` |
| `FCMS1645` | The query costs more than `MaxFieldCost` or `MaxTypeCost` |
| `FCMS1646` | `OnlyPersisted` is on and the query is not in the folder |
| `FCMS1647` | No persisted query has the id |
| `FCMS1648` | The request ran longer than `ExecutionTimeout` |

A query over the depth or the cost limit is refused before any field runs. Log event ids 9320 to 9329: 9320 the schema at start, 9321 a field that failed unexpectedly, 9322 a request refused by a limit.

## Introspection

The schema is one per instance, so introspection names every type and field, whatever the caller may read of them. It is open in Development. Elsewhere it needs a signed-in caller that holds `schema.read` in the site of the request, and any other caller gets `FCMS1643`. The schema download of HotChocolate and its IDE are not mapped.

## Persisted queries

With `PersistedQueries`, a request names a query of the folder by its id, the file name without `.graphql`:

```json
{ "id": "articles", "variables": { "locale": "fr" } }
```

The files are read once, at start. With `OnlyPersisted`, a request that carries a query of its own is refused (`FCMS1646`).

## Adding types from a plugin

A plugin that references this package adds types and fields with `IGraphQLSchemaContributor`, registered as a singleton:

```csharp
public sealed class ShopSchema : IGraphQLSchemaContributor
{
    public void Configure(ISchemaBuilder schema)
    {
        schema.AddType(new ObjectType<Basket>());
        schema.AddType(new ObjectTypeExtension(query =>
        {
            query.Name("Query");
            query.Field("basket").Type<ObjectType<Basket>>().Resolve(context => context.Service<Baskets>().CurrentAsync(context.RequestAborted));
        }));
    }
}

plugin.Services.AddSingleton<IGraphQLSchemaContributor, ShopSchema>();
```

A contributed resolver runs inside the operation of the request and checks its own permissions with `IPermissionChecker`.
