# 7. Localization & multi-site — approved

Localization is per field, with one row per locale sharing a document ID, so per-locale publishing and relational filtering stay native. Multi-site is a first-party plugin: one installation hosts several sites that share content types, media and users while keeping content, locales, settings and permissions per site. Multi-site is not multi-tenancy; tenants with isolated data are a later concern.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Locales per site | Must | Configurable locale list per site with a default locale; BCP 47 codes including custom and regional variants (`en-CA`, `fa-IR`). |
| 2 | Per-field localizable flag | Must | Fields marked localizable hold a value per locale; others are shared across locales. Media alt and caption follow the same rule. |
| 3 | Row-per-locale storage | Must | Each locale of an entry is a row sharing a document ID; status, versions and scheduling are per locale; non-localized fields are stored once. |
| 4 | Locale in every API | Must | `locale` parameter on REST, GraphQL, in-process and MCP; `locale=all` returns all locales; default locale when omitted. |
| 5 | Fallback chain | Must | Per site, ordered fallback (`fr-CA` → `fr` → `en`); applied per field in responses with a flag saying which locale served the value; `fallback=none` to disable. |
| 6 | Per-locale publish and schedule | Must | Publish, unpublish and schedule one locale independently of the others. |
| 7 | Locale-aware admin | Must | Locale switcher on entry and list views, translation status per locale, copy-from-locale, side-by-side view of two locales for translators. |
| 8 | Per-locale permissions | Must | Roles can be limited to read or write specific locales (a French translator role). |
| 9 | RTL and locale-aware fields | Must | RTL text direction in editors for RTL locales; date, number and currency formatting per locale in the admin. |
| 10 | Multi-site model | Must | Sites as a first-class object: name, hostnames, default locale, locales, settings; content types, media library and users are shared; entries, settings and roles are site-scoped. |
| 11 | Site context in APIs | Must | Site resolved by hostname, header or `site` parameter on Delivery and Management APIs; API keys bound to sites; GraphQL and MCP carry the same context. |
| 12 | Shared content across sites | Must | Content types may be declared global (one entry set for all sites) or per site; relations across sites allowed for global types. |
| 13 | Site switcher and per-site admin | Must | Users see only the sites they have roles in; settings pages are per site with instance-wide defaults. |
| 14 | Translation workflow | Should | Mark locales as needing translation when the source changes, assign translators, track outdated translations. |
| 15 | Per-site media folders and storage | Should | Optional site-scoped media roots and a different storage provider per site. |
| 16 | Locale-specific slugs and URL rules | Should | Slug per locale with uniqueness per locale and site; URL pattern per site for the rendering and preview plugins. |
| 17 | Export and import for translation | Should | XLIFF and JSON export of localizable fields per entry or in bulk, with re-import. |
| 18 | Cross-site copy | Should | Copy or move an entry between sites with relation remapping. |
| 19 | AI translation | Could | Draft translations through the AI assist plugin with glossary and style guide, always as a draft for review. |
| 20 | Machine-translation provider slot | Could | DeepL, Azure Translator and others behind one interface, used by the translation workflow. |
| 21 | Locale groups and regions | Could | Group locales (EMEA, LATAM) for permissions and bulk actions. |
| 22 | Site templates | Could | Create a new site from another site's settings, roles and seed content. |
| 23 | Multi-tenancy with isolated data | Won't | Separate schemas or databases per tenant is a later, separate plugin (see category 10). |
| 24 | Locale fallback that rewrites stored data | Won't | Fallback is applied on read only; stored values are never copied silently. |
| 25 | Separate installations per site as the multi-site answer | Won't | One installation serves many sites. |

## Decisions this category forces

- [ ] Site model placement: site in core (every entry has a site ID from day one) or added by the multi-site plugin. Recommendation: site ID in core with a default site, so single-site installs pay nothing and the plugin adds UI and hostname routing.
- [ ] Non-localized field storage: on a shared document row with locale rows for localized fields (recommended), or duplicated per locale row.
- [ ] Fallback semantics in lists: filter on the requested locale only, or on the resolved value after fallback. Recommendation: requested locale only, documented, because fallback-aware filtering defeats indexes.
- [ ] Locale code validation: strict BCP 47 or free-form codes. Recommendation: BCP 47 with a per-site allow-list.
