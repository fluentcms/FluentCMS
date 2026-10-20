// The admin's only JavaScript (ADR-0011): focus trapping, the roving tab index and global keyboard shortcuts,
// plus the native overlays, the theme and the reconnect display. It installs delegated listeners once per
// document and reads everything else from the DOM when an event arrives: no component makes an interop call to
// work, nothing has to be re-attached after Blazor renders or a reconnect, and focus, scroll and keys stay in
// the browser while the circuit is disconnected.

const supportsCommands = "command" in HTMLButtonElement.prototype;
const supportsAnchors = CSS.supports("position-area: block-end");

const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
const visible = (element) => element.offsetParent !== null || element.matches(":popover-open, dialog[open]");
const wrap = (index, length) => ((index % length) + length) % length;

// ---------------------------------------------------------------------------------------------
// Invoker Commands (commandfor / command) for browsers that do not implement them yet.

function runCommand(button) {
    const target = document.getElementById(button.getAttribute("commandfor"));
    if (!target) return;
    switch (button.getAttribute("command")) {
        case "show-modal": if (!target.open) target.showModal(); break;
        case "close": target.close(); break;
        case "show-popover": target.showPopover(); break;
        case "hide-popover": target.hidePopover(); break;
        case "toggle-popover": target.togglePopover(); break;
    }
}

// ---------------------------------------------------------------------------------------------
// Dialog: the top layer, Escape and focus containment are native. This adds the backdrop click
// and wraps Tab inside [data-trap-focus] so focus does not leave for the browser chrome.

function closeOnBackdropClick(event) {
    const dialog = event.target;
    if (!(dialog instanceof HTMLDialogElement) || !dialog.open || dialog.dataset.ui !== "dialog" || dialog.dataset.dismiss === "false") return;
    const rect = dialog.getBoundingClientRect();
    const inside = event.clientX >= rect.left && event.clientX <= rect.right && event.clientY >= rect.top && event.clientY <= rect.bottom;
    if (!inside) dialog.close();
}

function trapFocus(event, trap) {
    const focusable = [...trap.querySelectorAll(focusableSelector)].filter(visible);
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
    }
}

// ---------------------------------------------------------------------------------------------
// Roving tab index: one item of a group is in the tab order and the keys move focus between the
// items. Horizontal groups follow the direction, so the arrow pointing forward moves forward in RTL too.

function rove(items, index) {
    const next = items[wrap(index, items.length)];
    items.forEach((item) => { item.tabIndex = item === next ? 0 : -1; });
    next.focus();
    return next;
}

function roveIndex(event, index, count, axis) {
    const rtl = getComputedStyle(event.target).direction === "rtl";
    const forward = axis === "vertical" ? "ArrowDown" : rtl ? "ArrowLeft" : "ArrowRight";
    const backward = axis === "vertical" ? "ArrowUp" : rtl ? "ArrowRight" : "ArrowLeft";
    switch (event.key) {
        case forward: return index + 1;
        case backward: return index - 1;
        case "Home": return 0;
        case "End": return count - 1;
        default: return null;
    }
}

// ---------------------------------------------------------------------------------------------
// Menu: a popover with role=menu. Light dismiss and Escape are native; this adds the roving tab
// index, typeahead, Tab to leave, and the anchor positioning fallback.

const menuItems = (menu) => [...menu.querySelectorAll('[role="menuitem"]')].filter((item) => !item.hasAttribute("data-disabled"));
const menuTrigger = (menu) => document.querySelector(`[popovertarget="${menu.id}"]`);

function focusMenuItem(menu, index) {
    const items = menuItems(menu);
    if (items.length === 0) menu.focus();
    else rove(items, index);
}

function positionMenu(menu, trigger) {
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    const rtl = getComputedStyle(menu).direction === "rtl";
    menu.style.position = "fixed";
    menu.style.top = `${rect.bottom + 4}px`;
    menu.style.left = rtl ? "auto" : `${rect.left}px`;
    menu.style.right = rtl ? `${window.innerWidth - rect.right}px` : "auto";
}

function onMenuToggle(event) {
    const menu = event.target;
    if (!(menu instanceof HTMLElement) || menu.dataset.ui !== "menu") return;
    const trigger = menuTrigger(menu);
    if (event.newState === "open") {
        trigger?.setAttribute("aria-expanded", "true");
        if (!supportsAnchors) positionMenu(menu, trigger);
        focusMenuItem(menu, menu.dataset.openFocus === "last" ? -1 : 0);
        delete menu.dataset.openFocus;
    } else {
        trigger?.setAttribute("aria-expanded", "false");
        if (menu.contains(document.activeElement) || document.activeElement === document.body) trigger?.focus();
    }
}

function onMenuKeydown(event, menu) {
    const items = menuItems(menu);
    const index = items.indexOf(document.activeElement);
    const next = roveIndex(event, index, items.length, "vertical");
    if (next !== null) {
        event.preventDefault();
        focusMenuItem(menu, next);
        return true;
    }
    switch (event.key) {
        case "Tab": menu.hidePopover(); return false;
        case "Enter": case " ": event.preventDefault(); items[index]?.click(); return true;
    }
    if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
        const key = event.key.toLowerCase();
        const order = [...items.slice(index + 1), ...items.slice(0, index + 1)];
        const match = order.find((item) => item.textContent.trim().toLowerCase().startsWith(key));
        if (match) focusMenuItem(menu, items.indexOf(match));
        return true;
    }
    return false;
}

function onMenuTriggerKeydown(event, trigger) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return false;
    const menu = document.getElementById(trigger.getAttribute("popovertarget"));
    if (!menu) return false;
    event.preventDefault();
    menu.dataset.openFocus = event.key === "ArrowUp" ? "last" : "first";
    if (menu.matches(":popover-open")) focusMenuItem(menu, event.key === "ArrowUp" ? -1 : 0);
    // The source makes the trigger the menu's anchor, as opening through popovertarget does.
    else menu.showPopover({ source: trigger });
    return true;
}

// ---------------------------------------------------------------------------------------------
// Popover: light dismiss, Escape and focus return are native. This moves focus into it on open, as
// Radix does, keeps aria-expanded on the trigger, and positions it where anchor positioning is missing.

function onPopoverToggle(event) {
    const popover = event.target;
    if (!(popover instanceof HTMLElement) || popover.dataset.ui !== "popover") return;
    const trigger = menuTrigger(popover);
    if (event.newState === "open") {
        trigger?.setAttribute("aria-expanded", "true");
        if (!supportsAnchors) positionMenu(popover, trigger);
        const first = [...popover.querySelectorAll(focusableSelector)].find(visible);
        (first ?? popover).focus();
    } else {
        trigger?.setAttribute("aria-expanded", "false");
    }
}

// ---------------------------------------------------------------------------------------------
// Tooltip: a manual popover shown while its trigger is hovered or focused, hidden on leave, blur
// and Escape. The trigger is described by it whether it shows or not.

function tooltipFor(trigger) {
    return document.getElementById(trigger.dataset.tooltip);
}

function showTooltip(trigger) {
    const tooltip = tooltipFor(trigger);
    if (!tooltip || tooltip.matches(":popover-open")) return;
    tooltip.showPopover({ source: trigger });
    if (!supportsAnchors) positionTooltip(tooltip, trigger);
}

function hideTooltip(trigger) {
    const tooltip = tooltipFor(trigger);
    if (tooltip?.matches(":popover-open")) tooltip.hidePopover();
}

function positionTooltip(tooltip, trigger) {
    const rect = trigger.getBoundingClientRect();
    tooltip.style.position = "fixed";
    tooltip.style.top = `${rect.top - tooltip.offsetHeight - 4}px`;
    tooltip.style.left = `${rect.left + (rect.width - tooltip.offsetWidth) / 2}px`;
}

function onTooltipPointer(event) {
    const trigger = event.target.closest?.('[data-ui="tooltip-trigger"]');
    if (!trigger || trigger.contains(event.relatedTarget)) return;
    if (event.type === "pointerover") showTooltip(trigger);
    else hideTooltip(trigger);
}

function onTooltipFocus(event) {
    const trigger = event.target.closest?.('[data-ui="tooltip-trigger"]');
    if (!trigger) return;
    if (event.type === "focusin") showTooltip(trigger);
    else hideTooltip(trigger);
}

function hideTooltips() {
    document.querySelectorAll('[data-ui="tooltip"]:popover-open').forEach((tooltip) => tooltip.hidePopover());
}

// ---------------------------------------------------------------------------------------------
// Tabs: the active tab lives in the DOM, so switching needs no round trip. The arrow keys move and
// activate (Radix's automatic activation), following the direction; only the active tab is tabbable.

const tabsOf = (root) => [...root.querySelectorAll('[data-ui="tab"]')].filter((tab) => tab.closest('[data-ui="tabs"]') === root);

function activateTab(tab) {
    const root = tab.closest('[data-ui="tabs"]');
    if (!root || tab.disabled) return;
    for (const other of tabsOf(root)) {
        const active = other === tab;
        other.setAttribute("aria-selected", String(active));
        other.dataset.state = active ? "active" : "inactive";
        other.tabIndex = active ? 0 : -1;
        const panel = document.getElementById(other.getAttribute("aria-controls"));
        if (panel) {
            panel.hidden = !active;
            panel.dataset.state = active ? "active" : "inactive";
        }
    }
}

function onTabKeydown(event, tab) {
    const tabs = tabsOf(tab.closest('[data-ui="tabs"]')).filter((other) => !other.disabled);
    const next = roveIndex(event, tabs.indexOf(tab), tabs.length, "horizontal");
    if (next === null) return false;
    event.preventDefault();
    activateTab(rove(tabs, next));
    return true;
}

// ---------------------------------------------------------------------------------------------
// Command: an input with role=combobox over a listbox. Filtering, the active option, Enter and
// pointer selection stay in the browser; Blazor hears about a choice through the option's click.

const commandOptions = (list) => [...list.querySelectorAll('[data-ui="command-item"]')];
const activeOptions = (list) => commandOptions(list).filter((option) => !option.hidden && !option.hasAttribute("data-disabled"));

function commandParts(element) {
    const root = element.closest('[data-ui="command"]');
    return { input: root?.querySelector('[data-ui="command-input"]'), list: root?.querySelector('[data-ui="command-list"]') };
}

function selectOption(input, list, index) {
    const options = activeOptions(list);
    for (const option of commandOptions(list)) {
        option.removeAttribute("data-selected");
        option.setAttribute("aria-selected", "false");
    }
    if (options.length === 0) {
        input.removeAttribute("aria-activedescendant");
        return;
    }
    const next = wrap(index, options.length);
    options[next].setAttribute("data-selected", "true");
    options[next].setAttribute("aria-selected", "true");
    input.setAttribute("aria-activedescendant", options[next].id);
    revealOption(list, options[next]);
}

// Scrolls the list only: scrollIntoView would also scroll the page, which jumps to a palette further down on load.
function revealOption(list, option) {
    const bounds = list.getBoundingClientRect();
    const rect = option.getBoundingClientRect();
    if (rect.top < bounds.top) list.scrollTop -= bounds.top - rect.top;
    else if (rect.bottom > bounds.bottom) list.scrollTop += rect.bottom - bounds.bottom;
}

function filterCommand(input) {
    const { list } = commandParts(input);
    if (!list) return;
    const query = input.value.trim().toLowerCase();
    for (const option of commandOptions(list)) {
        const text = `${option.dataset.value ?? ""} ${option.dataset.keywords ?? ""} ${option.textContent}`.toLowerCase();
        option.hidden = query !== "" && !text.includes(query);
    }
    for (const group of list.querySelectorAll('[data-ui="command-group"]')) {
        group.hidden = !commandOptions(group).some((option) => !option.hidden);
    }
    const empty = list.querySelector('[data-ui="command-empty"]');
    if (empty) empty.hidden = commandOptions(list).some((option) => !option.hidden);
    selectOption(input, list, 0);
}

// Focus stays in the input, so the active option moves through aria-activedescendant instead of a roving tab index.
function onCommandKeydown(event, input) {
    const { list } = commandParts(input);
    if (!list) return false;
    const options = activeOptions(list);
    const index = options.findIndex((option) => option.hasAttribute("data-selected"));
    switch (event.key) {
        case "ArrowDown": selectOption(input, list, index + 1); break;
        case "ArrowUp": selectOption(input, list, index - 1); break;
        case "Home": selectOption(input, list, 0); break;
        case "End": selectOption(input, list, -1); break;
        case "Enter": options[index]?.click(); break;
        default: return false;
    }
    event.preventDefault();
    return true;
}

function onCommandPointerMove(event) {
    const option = event.target.closest?.('[data-ui="command-item"]');
    if (!option || option.hasAttribute("data-selected") || option.hasAttribute("data-disabled")) return;
    const { input, list } = commandParts(option);
    if (input && list) selectOption(input, list, activeOptions(list).indexOf(option));
}

// Blazor re-renders a list when its items change; the filter and the active option are re-applied.
const observer = new MutationObserver((mutations) => {
    const inputs = new Set();
    for (const mutation of mutations) {
        const { input } = commandParts(mutation.target);
        if (input) inputs.add(input);
        for (const node of mutation.addedNodes) {
            if (!(node instanceof Element)) continue;
            node.querySelectorAll('[data-ui="command-input"]').forEach((added) => inputs.add(added));
            showSingleKeys(node);
        }
    }
    inputs.forEach(filterCommand);
});

// ---------------------------------------------------------------------------------------------
// Global shortcuts: an element declares its keys in aria-keyshortcuts, ARIA's own attribute, so
// assistive technology announces them too: "Control+K Meta+K" is either chord. The page is read when a
// key is pressed, so a shortcut on content Blazor renders later, or again after a reconnect, works at
// once. A button or link is clicked and anything else is focused. A shortcut without Control, Alt or
// Meta waits while someone types in a field or a menu, and with a modal dialog open only the
// shortcuts inside it apply.

const modifiers = { control: "ctrlKey", alt: "altKey", shift: "shiftKey", meta: "metaKey" };

// WCAG 2.1 SC 2.1.4: a person can turn off every shortcut without Control, Alt or Meta, in the shortcuts dialog
// (ShortcutHelp.razor). The choice is kept in this browser, and storage that refuses leaves them on.
const singleKeysStorage = "fluentcms-single-key-shortcuts";

function singleKeysOn() {
    try {
        return localStorage.getItem(singleKeysStorage) !== "off";
    } catch {
        return true;
    }
}

function chooseSingleKeys(on) {
    try {
        if (on) localStorage.removeItem(singleKeysStorage);
        else localStorage.setItem(singleKeysStorage, "off");
    } catch {
        // The choice lasts for this page only.
    }
}

// The page renders the switch on; it shows the stored choice, again whenever Blazor renders it anew.
function showSingleKeys(root) {
    const toggles = [...(root.querySelectorAll?.('[data-ui="single-key-shortcuts"]') ?? [])];
    if (root.matches?.('[data-ui="single-key-shortcuts"]')) toggles.push(root);
    toggles.forEach((toggle) => { toggle.checked = singleKeysOn(); });
}
const editable = (element) => element.isContentEditable
    || element.matches('input:not([type="checkbox"], [type="radio"], [type="button"], [type="submit"], [type="reset"]), textarea, select');
const shown = (element) => (element.checkVisibility ? element.checkVisibility() : visible(element)) && !element.closest("[inert]");

function matchesShortcut(event, shortcut) {
    const parts = shortcut.split("+");
    const key = parts.pop().toLowerCase();
    const held = parts.map((part) => part.toLowerCase());
    if (key === "" || held.some((part) => !(part in modifiers))) return false;
    const name = key === "space" ? " " : key;
    // A letter or digit also matches by its physical key, so shortcuts work on other layouts and with Option on a Mac.
    const code = /^[a-z]$/.test(name) ? `Key${name.toUpperCase()}` : /^[0-9]$/.test(name) ? `Digit${name}` : null;
    if (event.key.toLowerCase() !== name && event.code !== code) return false;
    // A symbol such as ? already needs Shift on most layouts, so Shift is compared only for letters, digits and named keys.
    const symbol = name.length === 1 && code === null;
    return Object.entries(modifiers).every(([modifier, property]) => (modifier === "shift" && symbol) || event[property] === held.includes(modifier));
}

function onShortcutKeydown(event) {
    if (event.defaultPrevented || event.isComposing || event.repeat || ["Control", "Alt", "Shift", "Meta"].includes(event.key)) return false;
    const chord = event.ctrlKey || event.altKey || event.metaKey;
    if (!chord && (!singleKeysOn() || editable(event.target) || event.target.closest('[data-ui="menu"]'))) return false;
    const modal = document.querySelector("dialog:modal");
    for (const element of document.querySelectorAll("[aria-keyshortcuts]")) {
        if ((modal && !modal.contains(element)) || element.matches(":disabled") || !shown(element)) continue;
        if (!element.getAttribute("aria-keyshortcuts").split(/\s+/).some((shortcut) => matchesShortcut(event, shortcut))) continue;
        event.preventDefault();
        if (element.matches('button, a[href], summary, [role="button"], [role="menuitem"]')) element.click();
        else element.focus();
        return true;
    }
    return false;
}

// ---------------------------------------------------------------------------------------------
// Roving lists: a [data-ui="rove"] list holds [data-ui="rove-item"] elements, one of them in the tab
// order. J and K move to the next and previous item from anywhere outside a field, a menu or a modal
// that does not hold the list, so a reviewer steps through a queue or a set with one hand. The arrow
// keys, Home and End move while focus is in the list. A list Blazor is still filling is read again on
// every key, so items added in a later batch are reached too.

const roveItems = (list) => [...list.querySelectorAll('[data-ui="rove-item"]')].filter((item) => item.closest('[data-ui="rove"]') === list && shown(item));

function onRoveKeydown(event) {
    if (event.ctrlKey || event.altKey || event.metaKey || event.isComposing) return false;
    const target = event.target;
    const inside = target.closest('[data-ui="rove"]');
    // J and K also match their physical keys, as the other letters do, so they work on any layout.
    const key = event.key.toLowerCase();
    const step = key === "j" || event.code === "KeyJ" ? 1 : key === "k" || event.code === "KeyK" ? -1 : 0;

    if (step !== 0) {
        if (!singleKeysOn() || event.shiftKey || editable(target) || target.closest('[data-ui="menu"]')) return false;
        const modal = document.querySelector("dialog:modal");
        const list = inside ?? [...document.querySelectorAll('[data-ui="rove"]')].find((candidate) => (!modal || modal.contains(candidate)) && roveItems(candidate).length > 0);
        if (!list || (modal && !modal.contains(list))) return false;
        const items = roveItems(list);
        const current = items.indexOf(target.closest('[data-ui="rove-item"]'));
        // From outside the list, J enters at the item in the tab order and K at the last one.
        const start = items.findIndex((item) => item.tabIndex === 0);
        const index = current >= 0 ? current + step : step > 0 ? Math.max(start, 0) : items.length - 1;
        if (current >= 0 && (index < 0 || index >= items.length)) return true;
        event.preventDefault();
        rove(items, index).scrollIntoView({ block: "nearest" });
        return true;
    }

    // From a search box that controls a list, ArrowDown enters the list at its first item. Enter never submits the form around
    // the box: it chooses the only match, or enters the list when there are more.
    if (target.matches('[data-ui="rove-input"]') && (event.key === "ArrowDown" || event.key === "Enter")) {
        const list = document.getElementById(target.getAttribute("aria-controls"));
        // Matches for an earlier text are not the answer to what the box holds now, so Enter waits for the new ones.
        const fresh = list?.dataset.for === undefined || list.dataset.for === target.value.trim();
        const items = list && fresh ? roveItems(list) : [];
        if (event.key === "Enter") event.preventDefault();
        if (items.length === 0) return event.key === "Enter";
        event.preventDefault();
        if (event.key === "Enter" && items.length === 1) items[0].click();
        else rove(items, 0);
        return true;
    }

    if (!inside || !target.closest('[data-ui="rove-item"]')) return false;
    const items = roveItems(inside);
    const next = roveIndex(event, items.indexOf(target.closest('[data-ui="rove-item"]')), items.length, "vertical");
    if (next === null) return false;
    event.preventDefault();
    rove(items, Math.min(Math.max(next, 0), items.length - 1)).scrollIntoView({ block: "nearest" });
    return true;
}

// ---------------------------------------------------------------------------------------------
// Pickers (PickerField.razor): the search box owns its text, so a choice empties it here, and focus goes back to the box after a
// choice, Clear or Escape, before Blazor removes the element that had it.

const pickerBox = (element) => element.closest?.("[data-picker]")?.querySelector('[data-ui="rove-input"]');

function onPickerClick(event) {
    const option = event.target.closest?.('[data-picker] [role="option"]');
    const clear = event.target.closest?.('[data-picker] [data-action="clear"]');
    const box = pickerBox(option ?? clear ?? document.body);
    if (!box) return;
    if (option) box.value = "";
    box.focus();
}

function onPickerEscape(event) {
    if (event.key !== "Escape") return;
    const box = pickerBox(event.target);
    if (box && event.target !== box) box.focus();
}

// ---------------------------------------------------------------------------------------------
// Theme: the root carries "light" or "dark", or neither to follow the system. The choice goes into
// the cookie the host reads (ThemePreference.cs), so the next page arrives with the class already set.

const themeCookie = "fluentcms-theme";

function chooseTheme(theme) {
    const stored = theme === "light" || theme === "dark";
    document.documentElement.classList.remove("light", "dark");
    if (stored) document.documentElement.classList.add(theme);
    const secure = location.protocol === "https:" ? "; secure" : "";
    document.cookie = `${themeCookie}=${stored ? theme : ""}; path=/; max-age=${stored ? 31536000 : 0}; samesite=lax${secure}`;
}

// ---------------------------------------------------------------------------------------------
// Reconnect: Blazor reports its state as a class on #components-reconnect-modal (ReconnectModal.razor)
// and leaves the buttons, and the reload after a rejected circuit, to the page, as its default display does.
// A page whose form holds typed input is not reloaded, which would lose it: pickers stop searching, every field
// still posts, and the person saves the form or reloads it themselves.

const reconnectStates = ["show", "hide", "retrying", "failed", "rejected", "paused", "resume-failed", "lost"].map((state) => `components-reconnect-${state}`);

function showReconnectState(state) {
    const modal = document.getElementById("components-reconnect-modal");
    modal?.classList.remove(...reconnectStates);
    modal?.classList.add(`components-reconnect-${state}`);
}

const typed = (field) => field.type === "checkbox" || field.type === "radio"
    ? field.checked !== field.defaultChecked
    : field instanceof HTMLSelectElement
        ? [...field.options].some((option) => option.selected !== option.defaultSelected)
        : field.type !== "hidden" && field.value !== field.defaultValue;

function reloadOrKeepForm() {
    const fields = [...document.querySelectorAll("form input, form textarea, form select")];
    if (!document.querySelector("form [data-picker]") && !fields.some(typed)) {
        location.reload();
        return;
    }

    document.querySelectorAll('[data-picker] [data-ui="rove-input"], [data-picker] [role="option"]').forEach((control) => { control.disabled = true; });
    // Without a circuit nothing can render the next page's islands, so an enhanced navigation would keep this page's: every link
    // now loads its page in full, and so do Back and Forward, which Blazor handles without looking at that attribute.
    document.documentElement.setAttribute("data-enhance-nav", "false");
    // Loaded with GET, so going back to an entry a form's post made never asks to post it again.
    window.addEventListener("popstate", (event) => {
        event.stopImmediatePropagation();
        location.replace(location.href);
    }, { capture: true });
    showReconnectState("lost");
}

async function retryConnection() {
    document.removeEventListener("visibilitychange", retryWhenVisible);
    showReconnectState("show");
    try {
        if (await Blazor.reconnect() || await Blazor.resumeCircuit()) showReconnectState("hide");
        else reloadOrKeepForm();
    } catch {
        showReconnectState("failed");
        document.addEventListener("visibilitychange", retryWhenVisible);
    }
}

async function resumeConnection() {
    try {
        if (await Blazor.resumeCircuit()) showReconnectState("hide");
        else reloadOrKeepForm();
    } catch {
        showReconnectState("resume-failed");
    }
}

function retryWhenVisible() {
    if (document.visibilityState === "visible") retryConnection();
}

function onReconnectStateChanged(event) {
    if (event.detail?.state === "rejected") reloadOrKeepForm();
    if (event.detail?.state === "failed") document.addEventListener("visibilitychange", retryWhenVisible);
}

function onReconnectClick(button) {
    switch (button.dataset.reconnect) {
        case "retry": retryConnection(); break;
        case "resume": resumeConnection(); break;
        case "reload": location.reload(); break;
    }
}

// ---------------------------------------------------------------------------------------------

document.addEventListener("click", (event) => {
    const button = event.target.closest?.("button[commandfor]");
    if (button && !supportsCommands) runCommand(button);

    const tab = event.target.closest?.('[data-ui="tab"]');
    if (tab) activateTab(tab);

    const item = event.target.closest?.('[data-ui="menu-item"]');
    if (item && !item.hasAttribute("data-disabled")) item.closest('[data-ui="menu"]')?.hidePopover();

    const theme = event.target.closest?.("[data-theme-choice]");
    if (theme) chooseTheme(theme.dataset.themeChoice);

    const reconnect = event.target.closest?.("[data-reconnect]");
    if (reconnect) onReconnectClick(reconnect);

    onPickerClick(event);

    closeOnBackdropClick(event);
});

// Each handler says whether it used the key; a key none of them used reaches the focus trap, so Tab
// wraps inside a dialog whatever control has focus.
document.addEventListener("keydown", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) return;

    if (event.key === "Escape") hideTooltips();
    onPickerEscape(event);
    if (onShortcutKeydown(event)) return;
    if (onRoveKeydown(event)) return;

    const menu = target.closest('[data-ui="menu"]');
    if (menu && onMenuKeydown(event, menu)) return;

    const trigger = target.closest('[data-ui="menu-trigger"]');
    if (trigger && onMenuTriggerKeydown(event, trigger)) return;

    if (target.matches('[data-ui="command-input"]') && onCommandKeydown(event, target)) return;
    if (target.matches('[data-ui="tab"]') && onTabKeydown(event, target)) return;

    const trap = event.key === "Tab" ? target.closest("[data-trap-focus]") : null;
    if (trap) trapFocus(event, trap);
});

document.addEventListener("change", (event) => {
    if (event.target.matches?.('[data-ui="single-key-shortcuts"]')) chooseSingleKeys(event.target.checked);
});

document.addEventListener("input", (event) => {
    if (event.target.matches?.('[data-ui="command-input"]')) filterCommand(event.target);
});

document.addEventListener("pointermove", onCommandPointerMove);
document.addEventListener("pointerover", onTooltipPointer);
document.addEventListener("pointerout", onTooltipPointer);
document.addEventListener("focusin", onTooltipFocus);
document.addEventListener("focusout", onTooltipFocus);
document.addEventListener("toggle", onMenuToggle, true);
document.addEventListener("toggle", onPopoverToggle, true);
document.addEventListener("components-reconnect-state-changed", onReconnectStateChanged, true);
document.querySelectorAll('[data-ui="command-input"]').forEach(filterCommand);
showSingleKeys(document);
observer.observe(document.body, { childList: true, subtree: true });
