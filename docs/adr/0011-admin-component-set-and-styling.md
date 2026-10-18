# ADR-0011: Admin component set and styling

- Status: accepted
- Date: 2026-10-16, accepted 2026-10-17
- Related: ADR-0006, D02, D11, DSK-03, DSK-04, FRM-01, FRM-03, UX-02, UX-04, I18-N1, I18-N2, OPS-02, EXT-03 (M3)

## Context

ADR-0006 chose Blazor with interactive server rendering and deferred the component library and CSS approach. The admin serves 3 to 15 people on screens the project controls; site frontends never use it, because delivery is headless (D02). The review desk is keyboard-first, dense and diff-heavy (DSK-03, DSK-04, UX-02), and forms are generated from the project's own field types (FRM-01). The maintainer's preference is the shadcn design system.

shadcn in React is four things: components copied into the project and owned there, Radix headless primitives for behaviour, Tailwind for styling, and CSS variables for the theme. Its CLI generates React files only. What transfers to Blazor is the ownership model, the tokens and the class vocabulary; the behaviour layer has to come from somewhere else.

Constraints carried over from ADR-0006: permissive licences only, WCAG 2.1 AA, RTL, keyboard support, and a build that runs with the .NET SDK alone. In M3, plugins may contribute admin screens (EXT-03), so whatever the admin builds on becomes a surface plugin authors depend on.

## Decision

**A purpose-built component set, not a library.** Components live in `FluentCMS.Admin/Components/Ui/`, with no separate project and no package. Each component is self-contained: Razor markup carrying Tailwind classes in the shadcn style, parameters limited to what the screens use, no base-class hierarchy. A component is added only when a screen needs it, the same discipline as "no screen without a tool".

**Styling: Tailwind CSS v4 through the standalone CLI.** An MSBuild target in the Admin project downloads the pinned standalone binary once, caches it, and compiles the stylesheet before each build; a pre-downloaded path serves offline and CI builds. No Node, no npm, and no JavaScript Tailwind plugins, which the standalone binary cannot load and which shadcn on Tailwind v4 does not need.

**Theme: shadcn tokens verbatim.** The shadcn CSS variables (background, foreground, primary, muted, border, ring, radius and the rest) are ported as-is, with light and dark themes switched by a class on the root element and defaulting to the system preference. Class merging uses a C# port of tailwind-merge and a small variants helper in the CVA style, so shadcn documentation examples translate one-to-one into Razor.

**Behaviour: native HTML first.** Dialogs use `<dialog>`, popovers and menus use the Popover API, disclosure uses `<details>`. One small JavaScript interop module covers what the platform does not: focus trapping, roving tab index, and global keyboard shortcuts. Tables use Microsoft's QuickGrid styled with the shadcn table classes, which supplies virtualization and sorting. Icons are Lucide, rendered as inline SVG components generated from the icon set.

**Initial set.** Button, Input, Textarea, Select, Checkbox, Switch, RadioGroup, Badge, Card, Separator, Tabs, Dialog, Sheet, DropdownMenu, Popover, Tooltip, Command, Toast, Table, Kbd, FormField (label, description, error and the active rule hints required by FRM-03), and DiffView for the desk. Roughly twenty components; more only when a screen asks.

**Localization and RTL from the first component.** Strings go through `IStringLocalizer`. Layout uses Tailwind's logical utilities (start and end, not left and right) and the `dir` attribute on the root, so RTL is a flag rather than a rewrite (I18-N1, I18-N2).

**A showcase page.** An admin-only `/ui` page renders every component with its keyboard hints. It is the living documentation for contributors, the base for plugin authors in M3, and a visual regression target.

**Testing.** bUnit tests per component for rendering and keyboard behaviour; Playwright tests for the desk flows, including the keyboard-only ten-item review from UX-02.

## Decided by the spike

The spike in `docs/spikes/2026-10-admin-primitives.md` built Dialog, DropdownMenu and Command both ways: on native HTML with one interop module, and on Blazor Blueprint's headless layer (Apache 2.0), each styled with the same shadcn classes, and compared them on keyboard and screen-reader behaviour, RTL, behaviour over an interactive-server circuit including a forced reconnect, dependency size and the project's maintenance activity.

**The headless primitives are written in this repository.** Behaviour comes from the platform first: `<dialog>` for modals, the Popover API for menus and popovers, Invoker Commands to open them without a round trip, CSS anchor positioning to place them, and a listbox under a combobox for Command. `admin.js`, the one JavaScript module in the admin, adds what the platform lacks (focus wrapping, roving tab index, typeahead, client-side filtering) as delegated document listeners, so no component makes an interop call to work and the overlays keep working while the circuit is disconnected. Fallbacks for the two newest platform features live in the same module.

Blazor Blueprint was not chosen because its headless layer has no Command primitive, its overlays keep their open state on the server and need a portal host inside an interactive subtree (which per-page render modes make awkward), its package is sizeable for the two components it would cover, and it has shipped three major versions in eight months from one maintainer. The keyboard and screen-reader behaviour of the two variants was otherwise equivalent.

## Consequences

- The first weeks of UI work produce primitives rather than screens. The cost is bounded by native HTML and the spike.
- No runtime dependency on a UI library, full ownership of accessibility and keyboard behaviour, and shadcn documentation usable as-is by contributors.
- The standalone Tailwind binary is a sizeable one-time download per machine; CI caches it.
- Interactive server rendering round-trips interactions. The interop module keeps focus, scroll and key capture on the client so the desk stays responsive.
- Design-system creep is the main risk. The consumer-driven rule and the folder-not-package placement contain it. Whether the set ships as a package is decided in M3 with the plugin SDK (ADR-0004 publishes no packages in M1).

## Alternatives considered

- General Blazor libraries (MudBlazor, Fluent UI Blazor, Radzen): fast start, but a Material or Fluent look, a large theming surface, and the desk's keyboard and diff needs would still be custom.
- shadcn-style Blazor libraries (Blazor Blueprint, BlazorCN, NeoBlazorUI): the closest look, but young, and depending on their styled components contradicts the ownership model. Blueprint's headless layer was tried in the spike and not chosen; see above.
- Plain CSS with shadcn tokens and no Tailwind: no build step at all, but it loses the utility vocabulary that makes shadcn examples portable, and every screen takes longer.
- Tailwind through npm: the common path, but it breaks the .NET-SDK-only rule and fails in clean CI images without Node.
