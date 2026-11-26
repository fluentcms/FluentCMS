# Accessibility of the admin

The admin targets WCAG 2.2 at level AA (section 4.18 of the [admin technical specification](planning/admin-technical-spec.md#418-accessibility-baseline)). Most of it is checked on every full CI run; what a machine cannot hear can be checked by hand, with the script on this page. That pass is optional: no release of the admin waits on it.

## What CI checks

- **axe-core** checks every view of the admin in the light and the dark theme and right to left (`AccessibilityTests` of `admin/tests/FluentCMS.Admin.EndToEnd`, A-42).
- **The criteria axe-core does not decide** (`Wcag22`, A-54), in the light and the right-to-left sweeps: every target is 24 pixels or has the spacing WCAG 2.5.8 allows, the wrapped set's own controls included; with reduced motion asked for, no animation or transition runs; at 320 pixels of width no page scrolls sideways.
- **The daily loop by the keyboard alone** (`KeyboardLoopTests`, A-54): sign-in, search, a filter, every kind of field, a save, a conflict, a publish, a schedule, the preview, a bulk publish, an upload, the theme and sign-out, by Tab, the arrows, Enter, Space, Escape and typing. A control that Tab does not reach fails the case by name, any pointer event fails it, and at each stop of Tab the focused element must not be hidden under a sticky or fixed part of the page (WCAG 2.4.11).
- **Each drag has a way without dragging** (WCAG 2.5.7), listed in section 4.18 of the specification with the case that uses it.

## The screen reader pass

The pass is optional, and it has not been run so far: the admin's accessibility rests on the checks of CI above. Whoever runs it goes through the loop below with either pair of screen reader and browser, hears each step, and fills in the table. What the pass finds is filed as a bug with the label `area:admin`.

### Set-up

| | NVDA with Firefox | VoiceOver with Safari |
| --- | --- | --- |
| System | Windows 11 | macOS, the latest release |
| Screen reader | NVDA, the latest release, default settings | VoiceOver, default settings; the VoiceOver key (VO) is Control with Option |
| Browser | Firefox, the latest release | Safari, the latest release, with *Settings > Advanced > Press Tab to highlight each item on a webpage* on |
| Moving by control | Tab and Shift+Tab; NVDA switches to focus mode in a field on its own | Tab and Shift+Tab; VO with the arrows to read what is between |

Run the reference host in Development with the Blog sample, as the [admin README](../admin/README.md) says (`dotnet run --project src/FluentCMS.Host`, the admin at `https://localhost:5001/admin/`), and make an editor of the built-in role Editor with the host's `user create` command. Put an image file at hand for the upload. Use the keyboard alone: a step that needs the mouse is a finding.

### What to listen for

At each step the screen reader should say the control's **name**, its **role** and its **state** (checked, expanded, pressed, selected, required, invalid), and a result the admin announces should be read **once**, from the shell's polite region for a success and from its assertive region for an error. A screen reader words roles in its own way: NVDA says "edit" where VoiceOver says "text field", and "combo box" where VoiceOver says "pop-up button". A different word for the same role is no finding; a missing name, a wrong role or state, a result read twice or not at all, or focus that lands where nothing is said, is.

### The steps

| Step | Do | Hear |
| --- | --- | --- |
| 1. Sign in | Open the admin; on the hosted page Tab to the fields, type, press Enter | "User name or email, edit", "Password, edit, protected", "Sign in, button"; then the admin's home page |
| 2. Skip link | Press Tab once on the home page, then Enter | "Skip to main content, link"; focus moves to the main content |
| 3. Navigate | Tab to "Article" in the sidebar, press Enter | "Article, link" with "current page" once it is the page; then "Article, heading level 1" |
| 4. Search | Tab to the search box, type a word of a title, press Enter | "Search, search edit"; the grid "Article" with its rows, each row's checkbox "Select {title}" |
| 5. Filter | Tab to "Add filter", press Enter; choose Title, "starts with", type a value, Tab to "Apply filter", Enter; then Tab to the filter's remove button, Enter | "Add a filter, dialog" with focus inside; "Field, combo box", "Condition, combo box", "Value, edit"; back on the list, "Remove the filter: Title starts with …, button" |
| 6. Open | Tab to a row's checkbox, press Enter | The entry's title as "heading level 1" |
| 7. Text | Tab to Title, type | "Title, edit, required", its value; an error, when there is one, read with the field |
| 8. Date and time | Tab to "Published on", type a date, Tab, type a time | "Published on, edit", the example of the format and the time zone with each |
| 9. Select | Tab to Category, press the arrows | "Category, combo box", each option as it changes |
| 10. Relation | Tab to "Choose an entry", Enter; Tab to a row's checkbox, Space; Tab to "Choose", Enter | "Choose an entry: Author, dialog"; "Select {name}, checkbox, not checked", then "checked"; back on "Choose an entry, button", the chosen entry read |
| 11. Image | Tab to "Choose an asset", Enter; type in the search, Enter; Tab to a tile, Enter | The picker as a dialog with its name; each tile by its file name; back on "Choose an asset, button" |
| 12. Rich text | Tab into Body, type; press Alt+F10, the arrows, Enter | "Body, edit, multi line"; "Formatting of Body, toolbar", "Bold, B, toggle button, not pressed"; after Enter, back in the text |
| 13. Blocks | Tab to the block type, choose Quote, Tab to "Add Quote", Enter; on a block's title press Alt+Up arrow | "Add Quote, button"; the new block's title, such as "Quote 1, button, expanded", and "Quote 1 added."; after Alt+Up arrow, "{block} moved to position 1 of 2." read once |
| 14. Save | Tab to Save, press Enter | "Saved." read once, politely; focus stays on "Save, button" |
| 15. Conflict | Save the same entry in a second tab first, then save here | "Someone saved this entry first, dialog" with its text; "Keep mine, button"; back on Save after it |
| 16. Publish | Tab to Publish, Enter | "Published." read once |
| 17. Schedule | Tab to "Schedule", Enter; choose the action where it is offered, type a date and a time, Tab to Schedule, Enter | "Schedule a publish or an unpublish, dialog"; "Action, combo box" for a user who may also unpublish, "Date, edit, required", "Time, edit, required", the time zone; "Publish scheduled for …" read once |
| 18. Preview | Open an entry of a type with a preview template in the preview settings; Tab to Preview, Enter; Tab to Width, choose Tablet | Focus on the preview pane, read with its name; "Width, combo box" |
| 19. Bulk | On the list, Tab to "Select all rows on this page", Space; Tab to Actions, Enter; arrow to Publish, Enter; confirm | "Select all rows on this page, checkbox"; "Rows selected: {n}"; "Actions, menu button", "Publish, menu item"; the confirmation dialog with Cancel focused first; the progress "{n} of {total} done, {failed} failed." and the result "Done: {n} succeeded, {failed} failed." |
| 20. Upload | Tab to Media in the sidebar, Enter; Tab to "Upload files", Space; choose the file | "Upload files, button"; "Uploading", then "1 uploaded." |
| 21. Theme | Tab to the user menu, Enter; arrow to "Dark theme", Enter; Escape | "{your name}, menu button, collapsed"; "System theme, radio menu item, checked"; "Dark theme, radio menu item, checked" after Enter; back on the menu button, and the theme stays dark |
| 22. Dialog and drawer | Open `/system/components`; open the dialog, Tab to its close button, Enter; the same for the drawer | "Discard changes?, dialog", focus on its first control, "Discard, button"; Shift+Tab reaches "Close, button"; focus back on "Open dialog, button" and "Open drawer, button"; "Remove from the page" in the dialog also gives focus back |
| 23. Toasts | On the same page, press "Show error toast", then "Show toast" | "The entry could not be saved." read once and at once; "Saved." read once, politely; the error stays until its "Close, button" is pressed, the other goes after a few seconds |
| 24. Sign out | Open the user menu, arrow to "Sign out", Enter; on the hosted page Tab to "Sign out", Enter | "Sign out, menu item"; the hosted page asks; then its sign-in page |

A speech recognition user, such as Voice Control on macOS, may also check step 12: "Click Heading 1" and "Click H1" both reach the toolbar's button, since the letters it shows are in its name.

### The result

Copy this table into an issue with the label `area:admin`, with the versions used and the date. Write **pass**, or **fail** with the number of the bug filed; leave a step that was not run as **not run**, with the reason.

Versions: NVDA … with Firefox …; VoiceOver of macOS … with Safari …. Date: ….

| Step | NVDA with Firefox | VoiceOver with Safari | Notes |
| --- | --- | --- | --- |
| 1. Sign in | not run | not run | |
| 2. Skip link | not run | not run | |
| 3. Navigate | not run | not run | |
| 4. Search | not run | not run | |
| 5. Filter | not run | not run | |
| 6. Open | not run | not run | |
| 7. Text | not run | not run | |
| 8. Date and time | not run | not run | |
| 9. Select | not run | not run | |
| 10. Relation | not run | not run | |
| 11. Image | not run | not run | |
| 12. Rich text | not run | not run | |
| 13. Blocks | not run | not run | |
| 14. Save | not run | not run | |
| 15. Conflict | not run | not run | |
| 16. Publish | not run | not run | |
| 17. Schedule | not run | not run | |
| 18. Preview | not run | not run | |
| 19. Bulk | not run | not run | |
| 20. Upload | not run | not run | |
| 21. Theme | not run | not run | |
| 22. Dialog and drawer | not run | not run | |
| 23. Toasts | not run | not run | |
| 24. Sign out | not run | not run | |
