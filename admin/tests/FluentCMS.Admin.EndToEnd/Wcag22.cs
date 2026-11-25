using Microsoft.Playwright;

namespace FluentCMS.Admin.EndToEnd;

// The criteria that axe-core does not decide on a page as it stands (spec 4.18): the size of every target, the wrapped set's
// elements among them, whose role axe-core does not see (2.5.8), no motion when reduced motion is asked for, the width of 320
// pixels (1.4.10), and a focused element hidden under a sticky part (2.4.11), which the keyboard loop asks at each stop of
// Tab. Each check returns a line per fault, named by its view, as Axe.ViolationsAsync does.
public static class Wcag22
{
    // The narrowest window a page must fit without a sideways scroll (1.4.10).
    public const int ReflowWidth = 320;

    // The element's name in a fault: its tag, id or first class, and the start of its name or text.
    private const string Describe = """
        const describe = element => {
            if (!(element instanceof Element)) return String(element);
            const name = (element.getAttribute('aria-label') ?? element.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 40);
            const which = element.id ? '#' + element.id : element.classList.length ? '.' + element.classList[0] : '';
            return `${element.localName}${which}${name ? ` "${name}"` : ''}`;
        };
        """;

    // A target as 2.5.8 counts one: a control a pointer acts on, the wrapped set's elements among them, shown and enabled. A
    // link in a line of text is inline, which the criterion leaves out, and a control inside another counts as that one.
    private const string Targets = Describe + """
        const selector = 'a[href], button, input:not([type=hidden]), select, textarea, summary, [tabindex]:not([tabindex^="-"]), '
            + '[role=button], [role=link], [role=checkbox], [role=radio], [role=switch], [role=tab], [role=menuitem], '
            + '[role=menuitemradio], [role=menuitemcheckbox], [role=option], [role=treeitem], fluent-button, fluent-checkbox, '
            + 'fluent-switch, fluent-radio, fluent-menu-item, fluent-tab, fluent-tree-item';
        const inline = element => element.localName === 'a' && getComputedStyle(element).display === 'inline'
            && (element.parentElement?.textContent ?? '').trim().length > (element.textContent ?? '').trim().length;
        const shown = element => {
            const box = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return box.width > 1 && box.height > 1 && style.visibility !== 'hidden' && !element.closest('[inert], [aria-hidden=true]')
                && !element.matches(':disabled, [aria-disabled=true]');
        };
        // While a modal dialog of the library is open, the page under it is inert, so only the dialog's controls are targets.
        const modal = [...document.querySelectorAll('fluent-dialog, fluent-drawer')].find(host => host.shadowRoot?.querySelector('dialog')?.matches(':modal'));
        // A label that activates its control is part of the control's target.
        const area = element => {
            const box = element.getBoundingClientRect();
            const label = element.id ? document.querySelector(`label[for="${CSS.escape(element.id)}"]`) : null;
            const other = label?.getBoundingClientRect();
            if (!other || other.width === 0) return box;
            const left = Math.min(box.left, other.left), top = Math.min(box.top, other.top);
            const right = Math.max(box.right, other.right), bottom = Math.max(box.bottom, other.bottom);
            return { left, top, right, bottom, width: right - left, height: bottom - top };
        };
        const targets = [...document.querySelectorAll(selector)]
            .filter(element => !element.parentElement?.closest(selector) && shown(element) && !inline(element) && (!modal || modal.contains(element)))
            .map(element => ({ element, box: area(element) }));
        """;

    private const string TargetSizeScript = "() => {" + Targets + """
            const faults = [];
            const small = targets.filter(target => target.box.width < 23.5 || target.box.height < 23.5);
            for (const target of small) {
                const x = target.box.left + target.box.width / 2;
                const y = target.box.top + target.box.height / 2;
                // The spacing the criterion allows: a circle of 24 pixels on the target's middle meets no other target, nor
                // the circle of another small one.
                const near = targets.find(other => {
                    if (other === target) return false;
                    const dx = Math.max(other.box.left - x, 0, x - other.box.right);
                    const dy = Math.max(other.box.top - y, 0, y - other.box.bottom);
                    if (Math.hypot(dx, dy) < 12) return true;
                    const undersized = other.box.width < 23.5 || other.box.height < 23.5;
                    return undersized && Math.hypot(other.box.left + other.box.width / 2 - x, other.box.top + other.box.height / 2 - y) < 24;
                });
                if (near) {
                    faults.push(`${describe(target.element)} is ${Math.round(target.box.width)} by ${Math.round(target.box.height)} pixels, within 24 pixels of ${describe(near.element)}`);
                }
            }
            return faults;
        }
        """;

    // Every animation or transition under way, the library's shadow parts included, that lasts longer than the wrapped set's
    // 0.01 ms under reduced motion, or repeats without end.
    private const string MotionScript = "() => {" + Describe + """
            return document.getAnimations()
                .filter(animation => animation.playState === 'running' || animation.playState === 'pending')
                .map(animation => ({ animation, timing: animation.effect?.getComputedTiming() }))
                .filter(({ timing }) => !timing || timing.iterations === Infinity || Number(timing.duration) > 1)
                .map(({ animation, timing }) => `${animation.animationName ?? animation.transitionProperty ?? 'an animation'} of ${describe(animation.effect?.target)} runs ${timing?.duration} ms`);
        }
        """;

    // The page's scroll containers do not scroll sideways, but for a data table, whose layout of two dimensions the criterion
    // allows, and content that scrolls in a container of its own. The elements that stick out, outermost first, name the fault.
    private const string ReflowScript = "() => {" + Describe + """
            const roots = [document.scrollingElement, document.querySelector('.cms-root')].filter(Boolean);
            if (roots.every(root => root.scrollWidth <= root.clientWidth + 1)) return [];
            const width = document.documentElement.clientWidth;
            const scrolls = element => { for (let node = element.parentElement; node; node = node.parentElement) {
                if (roots.includes(node)) return false;
                if (['auto', 'scroll', 'hidden', 'clip'].includes(getComputedStyle(node).overflowX)) return true;
            } return false; };
            const out = [...document.querySelectorAll('.cms-root *')].filter(element => {
                const box = element.getBoundingClientRect();
                return box.width > 0 && (box.right > width + 1 || box.left < -1) && !scrolls(element) && !element.closest('table');
            });
            const outermost = out.filter(element => !out.includes(element.parentElement));
            return outermost.length === 0 ? [] : [`the page scrolls sideways at ${width} pixels: ${outermost.slice(0, 3).map(element => {
                const box = element.getBoundingClientRect();
                return `${describe(element)} from ${Math.round(box.left)} to ${Math.round(box.right)}`;
            }).join(', ')}`];
        }
        """;

    // The focused element, followed into the library's shadow parts, when every point of it in the window lies under a sticky
    // or fixed part of the page that does not hold it.
    private const string HiddenScript = Describe + """
        const holds = (outer, inner) => { for (let node = inner; node; node = node.parentNode ?? node.host) if (node === outer) return true; return false; };
        const deepest = (x, y) => { let hit = document.elementFromPoint(x, y);
            while (hit?.shadowRoot) { const deeper = hit.shadowRoot.elementFromPoint(x, y); if (!deeper || deeper === hit) break; hit = deeper; }
            return hit; };
        let focused = document.activeElement;
        while (focused?.shadowRoot?.activeElement) focused = focused.shadowRoot.activeElement;
        const hidden = () => {
            if (!focused || focused === document.body) return null;
            const box = focused.getBoundingClientRect();
            if (box.width < 1 || box.height < 1) return null;
            const points = [0.1, 0.5, 0.9].flatMap(fx => [0.1, 0.5, 0.9].map(fy => [box.left + box.width * fx, box.top + box.height * fy]))
                .filter(([x, y]) => x >= 0 && y >= 0 && x < innerWidth && y < innerHeight);
            let cover = null;
            for (const [x, y] of points) {
                const hit = deepest(x, y);
                if (!hit) continue;
                if (holds(focused, hit) || holds(hit, focused)) return null;
                cover = hit;
            }
            for (let node = cover; node instanceof Element; node = node.parentElement ?? node.getRootNode().host) {
                const position = getComputedStyle(node).position;
                if (position === 'sticky' || position === 'fixed') return `${describe(focused)} is hidden under ${describe(node)}`;
            }
            return null;
        };
        """;

    // The focused element's fault of 2.4.11, or null; for the keyboard loop's walk, which asks it with its own state.
    internal static string HiddenFocus => HiddenScript;

    public static async Task<IReadOnlyList<string>> TargetSizeAsync(IPage page, string view) =>
        Named(view, "target-size", await page.EvaluateAsync<string[]>(TargetSizeScript));

    public static async Task<IReadOnlyList<string>> MotionAsync(IPage page, string view) =>
        Named(view, "reduced-motion", await page.EvaluateAsync<string[]>(MotionScript));

    // The page at 320 pixels of width, then the window as it was, so that the view is left as the sweep found it.
    public static async Task<IReadOnlyList<string>> ReflowAsync(IPage page, string view)
    {
        var size = page.ViewportSize ?? new PageViewportSizeResult { Width = 1280, Height = 720 };
        await ResizeAsync(page, ReflowWidth, size.Height);
        try
        {
            return Named(view, "reflow", await page.EvaluateAsync<string[]>(ReflowScript));
        }
        finally
        {
            await ResizeAsync(page, size.Width, size.Height);
        }
    }

    // The window at the width, once the page's style has it: the media queries match it and a frame has been drawn, which
    // a resize the moment after another does not always have yet.
    private static async Task ResizeAsync(IPage page, int width, int height)
    {
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForFunctionAsync("width => matchMedia(`(width: ${width}px)`).matches", width);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    private static string[] Named(string view, string check, string[] faults) => [.. faults.Select(fault => $"{view}: {check}: {fault}")];
}
