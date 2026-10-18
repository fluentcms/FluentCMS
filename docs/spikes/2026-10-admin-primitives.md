# Spike: headless primitives for the admin

- Date: 2026-10-17
- Decides: the open point in ADR-0011 ("Open until the spike")
- Issue: #2434 (Admin shell and component set), first slice
- Branch: `2434-admin-spike`; commit `4595d282` holds both variants side by side, the pull request keeps the winner

## Question

ADR-0011 chose a purpose-built component set styled with the shadcn classes and left one point open: whether the behaviour layer (focus, menus, listbox) is written in this repository on native HTML, or built on the headless layer of Blazor Blueprint (`BlazorBlueprint.Primitives`, Apache-2.0). The spike built Dialog, DropdownMenu and Command both ways, with the same shadcn classes, on one page.

## What was built

The shell this spike needed is part of the result: `FluentCMS.Admin` is now a Razor class library that `FluentCMS.Host` serves as a Blazor Web App with interactive server rendering per page; Tailwind CSS v4 runs from the standalone binary through an MSBuild target (pinned version, SHA-256 checked, cached per machine, `TailwindExecutable` for offline builds); the shadcn "neutral" tokens are ported verbatim for light and dark, switched by a class on the root element, which also carries `dir`.

| | Own primitives (`Components/Ui`) | Blazor Blueprint (`Components/UiBlueprint`, deleted after the spike) |
| --- | --- | --- |
| Dialog | Native `<dialog>` opened with `commandfor`/`command="show-modal"`; the browser supplies the top layer, backdrop, Escape and focus containment; `admin.js` wraps Tab and closes on a backdrop click; `@onclose` tells the server | `BbDialog`, `BbDialogPortal`, `BbDialogOverlay`, `BbDialogContent`, `BbDialogClose`: a `div role="dialog"` rendered through a portal service, focus trap, scroll lock and Escape through the library's JS bundle; open state on the server |
| DropdownMenu | `popover="auto"` with `role="menu"`, opened by `popovertarget`; light dismiss and Escape are native; CSS anchor positioning on the invoker; `admin.js` adds the roving tab index, Home/End, typeahead and Tab-to-leave | `BbDropdownMenu`, `BbDropdownMenuTrigger`, `BbDropdownMenuContent`, `BbDropdownMenuItem`: Floating UI positioning in a portal, keyboard through the JS bundle, open state on the server |
| Command | `role="combobox"` input over a `role="listbox"`; filtering, the active option, Enter and pointer selection stay in the browser; the server hears a choice through the option's click | No Command primitive exists in the headless package (it is in the styled `BlazorBlueprint.Components` package). The variant was written in that package's shape: query and active option on the server, every keystroke a round trip, the library's JS bundle for the arrow-key guard |

Run it: `dotnet run --project src/FluentCMS.Host -- --environment Development`, open `/ui-spike`; `?dir=rtl` and `?theme=dark` switch direction and theme. Every action reports to a server-side status line, so a round trip is visible.

## Results

| Criterion (ADR-0011) | Own primitives | Blazor Blueprint | Observed with |
| --- | --- | --- | --- |
| Keyboard: Dialog | Focus lands on the first field, Tab wraps, Escape closes, focus returns to the trigger, close reaches the server | Same, except focus is left on `body` after Escape (not returned to the trigger) | Browser automation, `document.activeElement` after each key |
| Keyboard: DropdownMenu | Opens with focus on the first item, roving `tabindex` 0/-1, Arrow keys, Home/End, typeahead, Enter activates and closes, Escape closes, focus returns | Opens with focus on the menu container (`role="menu"`), items reachable with the arrows; otherwise the same | Same |
| Keyboard: Command | Arrow keys, Home/End, Enter, disabled options skipped, empty state; no round trip until a choice | Same keys, but every keystroke re-renders on the server | Same, plus the page's render counter |
| Screen reader (accessibility tree) | `button haspopup=dialog`; while the modal `dialog` (labelled by its title, described by its description) is open it is the only thing left in the tree, the page behind it is inert; `menu` with `menuitem` children, disabled item exposed; `combobox` with `listbox` and `option` children | Same roles and names; the dialog is a `div role=dialog aria-modal=true` at body level and the rest of the page stays in the tree behind it | Chrome accessibility tree snapshots |
| RTL (`dir="rtl"`) | Menu right-aligned to the trigger, shortcuts at inline-end, dialog close button at inline-end, headings start-aligned | Same (Floating UI honours `dir`) | Bounding boxes with `?dir=rtl` |
| Circuit: paused and resumed (`Blazor.pauseCircuit()`, `resumeCircuit()`) | Dialog, menu and command keep working while the circuit is gone; everything works after resume | Nothing opens while the circuit is gone; works after resume | Built-in browser |
| Circuit: forced reconnect (WebSocket closed under an open menu) | Arrow keys, dialog and command keep working during the gap; the dialog's close event queued and reached the same circuit after rejoin; the command's filter text survived | Overlays need the circuit for every open and close; fine once rejoined | Chrome with a wrapped `WebSocket` |
| Composition constraint | None | `BbPortalHost` must sit inside an interactive subtree; with per-page render modes (ADR-0006) the layout is static, so every interactive page or a wrapper has to host it | Build and run |
| Dependency size | No package. `admin.js`: 238 lines, 11 KB. Generated stylesheet for the whole page: 25 KB | `BlazorBlueprint.Primitives` 4.1.0: 565 KB package, 891 KB assembly, 31 JS files (286 KB) of which 22 (212 KB, Floating UI included) load on first use, 1 KB stylesheet, six scoped services. Transitive: only `Microsoft.AspNetCore.Components.Web` | NuGet package contents, network log |
| Code written | Dialog, DropdownMenu, Command: 320 lines of Razor plus the module | 351 lines of Razor wrappers to carry the shadcn classes and callbacks, plus the package | `wc -l` |
| Maintenance | Ours | Apache-2.0. Repository created 2025-11-14; 666 stars; 14 open and 160 closed issues; 386 merged pull requests; one author with 880 of about 930 commits; 53 package versions since 2026-02-02, three major versions in eight months (3.0.0 on 2026-02-17, 4.0.0 on 2026-09-19) with migration guides | GitHub and NuGet APIs on 2026-10-17 |

## Recommendation

Write the headless primitives in this repository, native HTML first, with `admin.js` as the one interop module. The case:

1. Blueprint's headless layer covers two of the three spike components. Command, the one the desk needs most, would be our code either way, or a dependency on the styled package and its five transitive packages, which ADR-0011 rules out.
2. The native variant is the better citizen of an interactive-server app. Its open and closed state lives in the DOM, so a menu, a dialog or a palette keeps working through a lost circuit and needs no portal host, no scoped services and no per-component JS interop. Blueprint keeps that state on the server and pays a round trip per open, close and keystroke.
3. The two variants have the same keyboard and screen-reader behaviour where it matters, and the native one got three details right that the library did not: the page behind a modal is inert for assistive technology too, focus lands on the first menu item, and focus returns to the trigger after Escape closes a dialog. The wrapper code to put the shadcn classes on Blueprint was as long as the primitives themselves.
4. Blueprint is young, moves fast and depends on one person. In M3 the admin becomes a surface plugins build on (EXT-03); three major versions in eight months is a cost the project would carry on their behalf.

What the native route costs: browser baselines. `<dialog>` and the Popover API are baseline in every current browser; Invoker Commands (`commandfor`) and CSS anchor positioning are newer (Chrome 125 and 135, Safari 26, recent Firefox), so `admin.js` carries a fallback for both. The admin serves a team on current browsers, and the fallbacks are small and removable.

## Follow-ups outside this pull request

- Theme default from the system preference and a stored preference; the root class comes from the query string until the shell stores it.
- The `/ui` showcase grows from `/ui-spike`, with the remaining components of the initial set, bUnit tests per component and the WCAG checks from the acceptance list.
- Entry and exit animations: either `@starting-style` transitions (the dialog already fades) or vendoring `tw-animate-css` so the shadcn animation classes apply.
- A C# port of `tailwind-merge`; `ClassNames.Cn` only joins for now.
