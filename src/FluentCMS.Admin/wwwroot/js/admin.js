// The admin's only JavaScript (ADR-0011). It installs delegated behaviour for the native overlays, the theme
// and the reconnect display once per document: no component makes an interop call to work, nothing has to be
// re-attached after Blazor re-renders, and everything here keeps working while the circuit is disconnected.

const supportsCommands = "command" in HTMLButtonElement.prototype;
const supportsAnchors = CSS.supports("position-area: block-end");

const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
const visible = (element) => element.offsetParent !== null || element.matches(":popover-open, dialog[open]");

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
// Menu: a popover with role=menu. Light dismiss and Escape are native; this adds the roving tab
// index, typeahead, Tab to leave, and the anchor positioning fallback.

const menuItems = (menu) => [...menu.querySelectorAll('[role="menuitem"]')].filter((item) => !item.hasAttribute("data-disabled"));
const menuTrigger = (menu) => document.querySelector(`[popovertarget="${menu.id}"]`);

function focusMenuItem(menu, index) {
    const items = menuItems(menu);
    if (items.length === 0) {
        menu.focus();
        return;
    }
    const next = ((index % items.length) + items.length) % items.length;
    items.forEach((item, i) => { item.tabIndex = i === next ? 0 : -1; });
    items[next].focus();
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
    switch (event.key) {
        case "ArrowDown": event.preventDefault(); focusMenuItem(menu, index + 1); return;
        case "ArrowUp": event.preventDefault(); focusMenuItem(menu, index - 1); return;
        case "Home": event.preventDefault(); focusMenuItem(menu, 0); return;
        case "End": event.preventDefault(); focusMenuItem(menu, -1); return;
        case "Tab": menu.hidePopover(); return;
        case "Enter": case " ": event.preventDefault(); items[index]?.click(); return;
    }
    if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
        const key = event.key.toLowerCase();
        const order = [...items.slice(index + 1), ...items.slice(0, index + 1)];
        const match = order.find((item) => item.textContent.trim().toLowerCase().startsWith(key));
        if (match) focusMenuItem(menu, items.indexOf(match));
    }
}

function onMenuTriggerKeydown(event, trigger) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
    const menu = document.getElementById(trigger.getAttribute("popovertarget"));
    if (!menu) return;
    event.preventDefault();
    menu.dataset.openFocus = event.key === "ArrowUp" ? "last" : "first";
    if (menu.matches(":popover-open")) focusMenuItem(menu, event.key === "ArrowUp" ? -1 : 0);
    // The source makes the trigger the menu's anchor, as opening through popovertarget does.
    else menu.showPopover({ source: trigger });
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
    const root = tab.closest('[data-ui="tabs"]');
    const tabs = tabsOf(root).filter((other) => !other.disabled);
    const index = tabs.indexOf(tab);
    const rtl = getComputedStyle(tab).direction === "rtl";
    let next;
    switch (event.key) {
        case "ArrowRight": next = index + (rtl ? -1 : 1); break;
        case "ArrowLeft": next = index + (rtl ? 1 : -1); break;
        case "Home": next = 0; break;
        case "End": next = tabs.length - 1; break;
        default: return;
    }
    event.preventDefault();
    const target = tabs[((next % tabs.length) + tabs.length) % tabs.length];
    target.focus();
    activateTab(target);
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
    const next = ((index % options.length) + options.length) % options.length;
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

function onCommandKeydown(event, input) {
    const { list } = commandParts(input);
    if (!list) return;
    const options = activeOptions(list);
    const index = options.findIndex((option) => option.hasAttribute("data-selected"));
    switch (event.key) {
        case "ArrowDown": event.preventDefault(); selectOption(input, list, index + 1); break;
        case "ArrowUp": event.preventDefault(); selectOption(input, list, index - 1); break;
        case "Home": event.preventDefault(); selectOption(input, list, 0); break;
        case "End": event.preventDefault(); selectOption(input, list, -1); break;
        case "Enter": event.preventDefault(); options[index]?.click(); break;
    }
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
            if (node instanceof Element) node.querySelectorAll('[data-ui="command-input"]').forEach((added) => inputs.add(added));
        }
    }
    inputs.forEach(filterCommand);
});

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

const reconnectStates = ["show", "hide", "retrying", "failed", "rejected", "paused", "resume-failed"].map((state) => `components-reconnect-${state}`);

function showReconnectState(state) {
    const modal = document.getElementById("components-reconnect-modal");
    modal?.classList.remove(...reconnectStates);
    modal?.classList.add(`components-reconnect-${state}`);
}

async function retryConnection() {
    document.removeEventListener("visibilitychange", retryWhenVisible);
    showReconnectState("show");
    try {
        if (await Blazor.reconnect() || await Blazor.resumeCircuit()) showReconnectState("hide");
        else location.reload();
    } catch {
        showReconnectState("failed");
        document.addEventListener("visibilitychange", retryWhenVisible);
    }
}

async function resumeConnection() {
    try {
        if (await Blazor.resumeCircuit()) showReconnectState("hide");
        else location.reload();
    } catch {
        showReconnectState("resume-failed");
    }
}

function retryWhenVisible() {
    if (document.visibilityState === "visible") retryConnection();
}

function onReconnectStateChanged(event) {
    if (event.detail?.state === "rejected") location.reload();
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

    closeOnBackdropClick(event);
});

document.addEventListener("keydown", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) return;

    if (event.key === "Escape") hideTooltips();

    const menu = target.closest('[data-ui="menu"]');
    if (menu) return onMenuKeydown(event, menu);

    const trigger = target.closest('[data-ui="menu-trigger"]');
    if (trigger) return onMenuTriggerKeydown(event, trigger);

    if (target.matches('[data-ui="command-input"]')) return onCommandKeydown(event, target);

    if (target.matches('[data-ui="tab"]')) return onTabKeydown(event, target);

    const trap = event.key === "Tab" ? target.closest("[data-trap-focus]") : null;
    if (trap) trapFocus(event, trap);
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
observer.observe(document.body, { childList: true, subtree: true });
