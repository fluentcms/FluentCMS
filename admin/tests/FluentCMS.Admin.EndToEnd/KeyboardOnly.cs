using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A tab driven by the keyboard alone (spec 4.18, 7). Focus moves by Tab and Shift+Tab only, as it does for a user without a
// pointer, so a control that Tab does not reach fails its step by name, with the stops Tab made. Every trusted pointer event
// of the tab's pages is recorded, so that a step a click alone reaches cannot pass unseen. At each stop the focused element
// must not be hidden under a sticky or fixed part of the page (WCAG 2.4.11), and Tab must move it, so that no control traps
// it. A focus a script gives, as Ui.FocusAsync does, is not used.
public sealed class KeyboardOnly
{
    // Further than the longest walk of the admin's pages: a control so many stops away is not reached in practice.
    private const int MostStops = 90;

    private const string Binding = "fluentcmsPointerEvent";

    // The events a pointer sends: a press, a move over the page, a drag, a wheel; and a click with a count of presses, which a
    // click by Enter or Space has not.
    private const string Watch = """
        (() => {
            const name = target => target instanceof Element ? target.localName + (target.id ? '#' + target.id : '') : String(target);
            for (const type of ['pointerdown', 'pointerup', 'pointermove', 'mousedown', 'mouseup', 'mousemove', 'touchstart', 'dragstart', 'wheel', 'contextmenu', 'dblclick']) {
                window.addEventListener(type, event => { if (event.isTrusted) window.fluentcmsPointerEvent(`${type} on ${name(event.target)} at ${location.pathname}`); }, { capture: true, passive: true });
            }
            window.addEventListener('click', event => { if (event.isTrusted && event.detail > 0) window.fluentcmsPointerEvent(`click on ${name(event.target)} at ${location.pathname}`); }, { capture: true });
        })();
        """;

    // Where focus stands against the target: on it or in it, and otherwise the way to it in the order of the document, with the
    // stop's name, whether Tab left it where it was, and the fault of 2.4.11.
    private static readonly string Stop = "target => {" + Wcag22.HiddenFocus + """
            const active = document.activeElement;
            const same = active !== document.body && window.fluentcmsLastStop === active;
            window.fluentcmsLastStop = active;
            return {
                reached: !!active && (active === target || target.contains(active)),
                backward: !!active && active !== document.body && !!(active.compareDocumentPosition(target) & Node.DOCUMENT_POSITION_PRECEDING),
                name: focused ? describe(focused) : 'nothing',
                same,
                hidden: hidden(),
            };
        }
        """;

    private readonly ConcurrentQueue<string> _pointer = new();
    private readonly ConcurrentQueue<string> _hidden = new();

    private KeyboardOnly(AdminTab tab) => Tab = tab;

    public AdminTab Tab { get; }

    public IKeyboard Keys => Tab.Page.Keyboard;

    // The trusted pointer events the tab's pages have seen, on every page it loaded.
    public IReadOnlyCollection<string> PointerEvents => _pointer;

    // The stops whose focused element lay under a sticky or fixed part.
    public IReadOnlyCollection<string> HiddenStops => _hidden;

    // The tab, watched from its next page on.
    public static async Task<KeyboardOnly> WatchAsync(AdminTab tab)
    {
        var keyboard = new KeyboardOnly(tab);
        await tab.Page.ExposeFunctionAsync<string>(Binding, keyboard._pointer.Enqueue);
        await tab.Page.AddInitScriptAsync(Watch);
        return keyboard;
    }

    // Moves focus to the target by Tab, or by Shift+Tab when it comes earlier in the document, once it shows.
    public async Task TabToAsync(ILocator target, int most = MostStops)
    {
        await Expect(target).ToBeVisibleAsync();
        bool? backward = null;
        var stops = new List<string>();
        for (var stop = 0; stop <= most; stop++)
        {
            var state = await target.EvaluateAsync<JsonElement>(Stop);
            if (state.GetProperty("hidden").GetString() is { } hidden)
            {
                _hidden.Enqueue(hidden);
            }

            if (state.GetProperty("reached").GetBoolean())
            {
                return;
            }

            var name = state.GetProperty("name").GetString() ?? "";
            stops.Add(name);
            if (stop > 0 && state.GetProperty("same").GetBoolean())
            {
                throw new ShouldAssertException($"Tab left focus on {name}, which traps it, on the way to {target}. The stops: {string.Join(" > ", stops)}");
            }

            backward ??= state.GetProperty("backward").GetBoolean();
            await Keys.PressAsync(backward.Value ? "Shift+Tab" : "Tab");
        }

        throw new ShouldAssertException($"{target} was not reached by {most} presses of {(backward == true ? "Shift+Tab" : "Tab")}. The stops: {string.Join(" > ", stops)}");
    }

    // Moves focus along a composite control, such as a menu or a list of options, by the key until the item has it.
    public async Task ArrowToAsync(ILocator item, string key = "ArrowDown")
    {
        await Expect(item).ToBeVisibleAsync();
        for (var press = 0; press <= MostStops; press++)
        {
            if (await item.EvaluateAsync<bool>("item => item === document.activeElement || item.contains(document.activeElement)"))
            {
                return;
            }

            await Keys.PressAsync(key);
        }

        throw new ShouldAssertException($"{item} was not reached by {MostStops} presses of {key}.");
    }

    // Tab to the target, then the key.
    public async Task PressOnAsync(ILocator target, string key)
    {
        await TabToAsync(target);
        await Keys.PressAsync(key);
    }

    // The target's text replaced by the text, typed key by key.
    public async Task TypeIntoAsync(ILocator target, string text)
    {
        await TabToAsync(target);
        await Keys.PressAsync("Control+a");
        await Keys.TypeAsync(text);
    }

    // Focus is inside the scope, such as a dialog that has just opened.
    public static Task FocusInsideAsync(ILocator scope) =>
        Ui.UntilAsync(() => scope.EvaluateAsync<bool>("scope => scope.contains(document.activeElement)"), $"focus never moved into {scope}");

    // The case's own end: no pointer event, and no focused element under a sticky part.
    public void ShouldHaveUsedTheKeyboardAlone()
    {
        PointerEvents.ShouldBeEmpty("a pointer event reached the page, so a step may be reachable by click alone");
        HiddenStops.ShouldBeEmpty("a focused element lay under a sticky part of the page (WCAG 2.4.11)");
    }
}
