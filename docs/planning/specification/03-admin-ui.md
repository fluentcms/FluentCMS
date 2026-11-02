# 3. Admin UI — approved

A Blazor WebAssembly app generated from the schema and driven only by the Management API. The first release targets the editor's daily loop: find an entry, edit it with confidence, preview it, publish it. Everything a plugin can add goes through the extension points in category 8.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Admin shell | Must | Sidebar by content type and plugin section, site switcher, user menu, breadcrumbs, responsive layout usable on a tablet, dark and light themes. |
| 2 | Runtime configuration | Must | API base URL, OIDC client and branding read from `config.json` at startup, so the prebuilt static admin works against any API host. |
| 3 | Login and session | Must | Authorization Code + PKCE against the API's authorization server, silent refresh, external SSO buttons when configured, graceful token expiry with draft preserved. |
| 4 | Generated list views | Must | Per type: configurable columns, multi-sort, filters built from the field types, free-text search, pagination, status and locale badges, row selection. |
| 5 | Generated edit forms | Must | Per type from field metadata: groups and tabs, inline validation with API error mapping, dirty-state guard, keyboard shortcuts for save and publish. |
| 6 | Field editors for every core field type | Must | Text, long text, numbers, boolean, date and time pickers, select, slug with auto-generate and lock, JSON editor with validation, email, URL. |
| 7 | Rich text editor | Must | Structured editor producing the JSON format from category 1: marks, headings, lists, links to entries and URLs, media embeds, typed inline blocks, paste cleanup, Markdown shortcuts. |
| 8 | Component and block editors | Must | Collapsible component groups; block list with add-by-type picker, drag reorder, duplicate, collapse all, block previews. |
| 9 | Relation picker | Must | Search, filter and multi-select across types, ordering for multi relations, inline create of a related entry, open related entry in a side panel. |
| 10 | Media picker | Must | Browse and search the media library, upload inline, folder navigation, focal point and alt text editing from the picker. |
| 11 | Draft, publish and status controls | Must | Save draft, publish, unpublish, discard changes, Modified indicator, per-locale status where applicable. |
| 12 | Version history panel | Must | List of versions with author, time and origin; side-by-side diff; restore. |
| 13 | Preview | Must | Iframe preview of the current entry with draft toggle, device breakpoints and per-type preview URL resolver; works with external frontends via preview key. |
| 14 | Bulk actions | Must | Publish, unpublish, delete and plugin actions on selected rows, with confirmation and progress reporting. |
| 15 | Media library | Must | Grid and list, folders, drag-and-drop and paste upload, metadata editing, usage (where is this file referenced), replace file. |
| 16 | Users, roles and API keys | Must | Manage admin users, roles and permissions, API keys with scopes and expiry. |
| 17 | Settings | Must | Site settings, locales, plugin settings pages, webhooks, MCP clients. |
| 18 | Admin localization | Must | Resource-based translations of the admin UI, RTL layout, date and number formats per user. |
| 19 | Accessibility baseline | Must | Keyboard navigation for all core flows, focus management, ARIA on generated controls, visible focus; WCAG 2.2 AA as the target. |
| 20 | Autosave of drafts | Should | Debounced autosave with conflict detection against the entry's version. |
| 21 | Document locking indicator | Should | Who else has the entry open, with take-over; needs the SignalR channel. |
| 22 | Saved views | Should | Named filter, sort and column sets per type, private or shared with roles. |
| 23 | Live preview | Should | Side-by-side preview updating with unsaved changes via `postMessage`. |
| 24 | Dashboard | Should | Home page with recent activity, drafts awaiting the user, scheduled items and plugin widgets. |
| 25 | Command palette | Should | Keyboard-first navigation and actions. |
| 26 | Activity log viewer | Should | Filterable view of the activity log with origin (admin, API, MCP, job). |
| 27 | Trash | Should | Soft-deleted entries and media with restore. |
| 28 | Alternative layouts | Could | Kanban by status, calendar by date, card grid. |
| 29 | Comments and mentions | Could | Threaded comments on entries and fields with notifications. |
| 30 | Visual click-to-edit overlays | Could | Data-attribute protocol so a frontend can deep-link into the field being viewed. |
| 31 | White-label theming | Could | Logo, colors and login page per site. |
| 32 | Per-field accept or reject on publish | Could | Compare draft to published and choose per field. |
| 33 | Realtime co-editing with presence | Won't | Patch streaming and cursors. |
| 34 | iframe or JavaScript field editors | Won't | Blazor components only (category 8). |
| 35 | Admin served by Blazor Server | Won't | WebAssembly only. |

## Decisions this category forces

- [ ] Component library: a commercial-free open source set (MudBlazor, Fluent UI Blazor, Radzen community) or an in-house minimal set over Tailwind. Recommendation: Fluent UI Blazor for accessibility and Microsoft alignment, wrapped behind FluentCMS components so it can be swapped.
- [ ] Rich text editor engine: Tiptap/ProseMirror via JS interop (mature, matches the JSON format decision) or a pure Blazor editor (no interop, far more work). Recommendation: Tiptap via interop, as Umbraco does.
- [ ] Preview protocol: per-type preview URL template with a signed preview token, plus `postMessage` for live preview later. Recommendation: decide the message contract now so live preview is additive.
- [ ] Lazy loading granularity: per plugin assembly, or per admin area. Recommendation: per plugin assembly using Blazor's `LazyAssemblyLoader`.
