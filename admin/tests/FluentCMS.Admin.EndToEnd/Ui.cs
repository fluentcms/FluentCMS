using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The one copy of what the browser tests do the same way. It is one of the few files of the harness that may wait by a timer
// or read a clock, which the architecture tests name (EndToEndRulesTests), and they hold every other file to these helpers
// (spec 7).
public static class Ui
{
    // Past the component library's second focus move, 25 ms after a dialog closes (FocusOnPreviousActiveElement), and the
    // admin's own, 100 ms after it (DocumentInterop.DialogFocusDelay). A timer of the page set now runs after both, since
    // timers run in the order they expire.
    private const int FocusSettle = 150;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // How long a control that shows may take to take focus and keep it.
    private static readonly TimeSpan FocusWindow = TimeSpan.FromSeconds(2);

    // Focus as Tab would give it. A control in a dialog that has just shown may not take focus yet, and the dialog's own focus
    // move may take it once, so focus is asked for once the control shows, and again until the control has it and still has
    // it after the page's next timers, for two seconds at most. A control that loses focus again and again fails, by name: a
    // keyboard user could not use it either.
    public static async Task FocusAsync(ILocator target)
    {
        await Expect(target).ToBeVisibleAsync();
        var deadline = DateTime.UtcNow + FocusWindow;
        while (true)
        {
            await target.FocusAsync();
            if (await HasFocusAsync(target))
            {
                await PageTimerAsync(target.Page, FocusSettle);
                if (await HasFocusAsync(target))
                {
                    return;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"{target} did not take focus and keep it within {FocusWindow.TotalSeconds:0} s of showing.");
            }

            await PageTimerAsync(target.Page, 50);
        }
    }

    // Focus, then a key: no pointer is used.
    public static async Task PressAsync(ILocator target, string key)
    {
        await FocusAsync(target);
        await target.Page.Keyboard.PressAsync(key);
    }

    // A dialog gives focus back to its opener as it closes, and the library and the admin do it again on their timers, which
    // would take it from the next control; focus is where it stays once those have run.
    public static async Task FocusReturnedAsync(ILocator opener)
    {
        await Expect(opener).ToBeFocusedAsync();
        await PageTimerAsync(opener.Page, FocusSettle);
        await Expect(opener).ToBeFocusedAsync();
    }

    // Drags the source onto the target with the pointer and lets go only once the target shows, by its class, that the editor
    // would drop there: a drag let go before the editor has seen it over the target drops nothing on a slow machine. The
    // browser raises dragover only as the pointer moves, so the pointer moves a pixel over the target until the editor has it.
    // A source whose middle is a control, such as a button, which starts no drag, is held by its edge instead. A drag that never
    // gets there fails with what the page saw of it (#3659).
    public static async Task DragAsync(ILocator source, ILocator target, string dropClass, bool byEdge = false)
    {
        var page = source.Page;
        var drop = new Regex($@"(^|\s){Regex.Escape(dropClass)}(\s|$)");
        string? unseen;
        await using (var targetElement = await target.ElementHandleAsync())
        {
            await source.EvaluateAsync(WatchDrag, targetElement);
            unseen = await source.EvaluateAsync<string?>(BringIntoView, new { target = targetElement, byEdge });
        }

        if (unseen is not null)
        {
            throw new ShouldAssertException($"{source} and {target} cannot both be in view for a drag between them: {unseen}.");
        }

        if (byEdge)
        {
            var held = (await source.BoundingBoxAsync()).ShouldNotBeNull();
            await source.HoverAsync(new LocatorHoverOptions { Position = new Position { X = 2, Y = held.Height / 2 } });
        }
        else
        {
            await source.HoverAsync();
        }

        await page.Mouse.DownAsync();
        var box = (await target.BoundingBoxAsync()).ShouldNotBeNull();
        var (x, y) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await page.Mouse.MoveAsync(x, y, new MouseMoveOptions { Steps = 10 });
        var nudge = 0;
        try
        {
            await UntilAsync(
                async () =>
                {
                    if (drop.IsMatch(await target.GetAttributeAsync("class") ?? ""))
                    {
                        return true;
                    }

                    await page.Mouse.MoveAsync(x + (++nudge % 2), y);
                    return false;
                },
                $"{target} never showed that the editor would take the drop");
        }
        catch (ShouldAssertException exception)
        {
            string seen;
            try
            {
                // Playwright passes a float to the page as an object, not a number.
                seen = await page.EvaluateAsync<string>(DragSeen, new { x = (double)x, y = (double)y });
            }
            catch (PlaywrightException unread)
            {
                seen = $"The page could not say what it saw of the drag: {unread.Message}";
            }

            throw new ShouldAssertException(
                $"{target} never showed that the editor would take the drop within {AdminBrowser.Wait.TotalSeconds:0} s, the pointer held at ({x:0.#}, {y:0.#}). {seen}",
                exception);
        }

        await page.Mouse.UpAsync();
    }

    // The pointer reaches only what the viewport shows, and hovering the source brings only the source into view, so the pair
    // is centred in the source's scrolling box first (#3659). Returns why the points the drag presses and goes to are not both
    // shown, or null.
    private const string BringIntoView = """
        (source, { target, byEdge }) => {
          let scroller = document.scrollingElement;
          for (let element = source.parentElement; element; element = element.parentElement) {
            if (/(auto|scroll|overlay)/.test(getComputedStyle(element).overflowY) && element.scrollHeight > element.clientHeight) {
              scroller = element;
              break;
            }
          }
          const view = scroller === document.scrollingElement ? { top: 0, height: innerHeight } : { top: scroller.getBoundingClientRect().top + scroller.clientTop, height: scroller.clientHeight };
          source.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" });
          const before = [source.getBoundingClientRect(), target.getBoundingClientRect()];
          const middle = (Math.min(before[0].top, before[1].top) + Math.max(before[0].bottom, before[1].bottom)) / 2;
          scroller.scrollBy({ top: middle - (view.top + view.height / 2), behavior: "instant" });
          const [held, over] = [source.getBoundingClientRect(), target.getBoundingClientRect()];
          const press = { x: byEdge ? held.left + 2 : held.left + held.width / 2, y: held.top + held.height / 2 };
          const drop = { x: over.left + over.width / 2, y: over.top + over.height / 2 };
          const shown = (point, element) => point.x >= 0 && point.y >= 0 && point.x < innerWidth && point.y < innerHeight && element.contains(document.elementFromPoint(point.x, point.y));
          if (shown(press, source) && shown(drop, target)) {
            return null;
          }
          const box = r => `(${Math.round(r.x)}, ${Math.round(r.y)}, ${Math.round(r.width)} x ${Math.round(r.height)})`;
          return `after scrolling, the source's box is at ${box(held)} and the target's at ${box(over)}, in a viewport of ${innerWidth} x ${innerHeight}`;
        }
        """;

    // What the page saw of a drag, kept from before the press, for the message of a drag that never reached its target: whether
    // the browser started the drag, where it raised its events and which the page took, and what the pointer is over.
    private const string WatchDrag = """
        (source, target) => {
          const watch = window.__fluentcmsDragWatch ??= (() => {
            const kept = { events: [] };
            for (const type of ["pointerdown", "mousedown", "mousemove", "dragstart", "dragenter", "dragover", "dragleave", "drop", "dragend", "mouseup"]) {
              window.addEventListener(type, event => { if (kept.events.length < 5000) kept.events.push({ type, event, at: performance.now() }); }, true);
            }
            return kept;
          })();
          watch.events = [];
          watch.source = source;
          watch.target = target;
        }
        """;

    private const string DragSeen = """
        ({ x, y }) => {
          const watch = window.__fluentcmsDragWatch;
          if (!watch) {
            return "The page holds no record of the drag: it was loaded again after the press.";
          }
          const name = node => node instanceof Element ? node.localName + [...node.classList].map(c => "." + c).join("") : node === window ? "the window" : String(node?.nodeName);
          const where = node => !(node instanceof Node) ? name(node) : watch.source.contains(node) ? "the source" : watch.target.contains(node) ? "the target" : name(node);
          const state = (element, what) => `${what}${element.isConnected ? "" : ", no longer in the document,"} has class "${element.getAttribute("class") ?? ""}" and draggable "${element.getAttribute("draggable") ?? ""}"`;
          const steps = [];
          for (const { type, event, at } of watch.events) {
            const step = `${type} on ${where(event.target)}${event.defaultPrevented ? " (default prevented)" : ""}`;
            const last = steps[steps.length - 1];
            if (last?.step === step) {
              last.count++;
            } else {
              steps.push({ step, count: 1, at: Math.round(at - watch.events[0].at) });
            }
          }
          const seen = steps.length === 0 ? "The page saw no event of the drag" : "The page saw " + steps.slice(0, 80).map(s => `${s.step}${s.count > 1 ? " x" + s.count : ""} at ${s.at} ms`).join(", ") + (steps.length > 80 ? ", and more" : "");
          const box = watch.target.getBoundingClientRect();
          const at = document.elementFromPoint(x, y);
          return `${seen}. ${state(watch.source, "The source")}; ${state(watch.target, "the target")}, with its box at (${Math.round(box.x)}, ${Math.round(box.y)}, ${Math.round(box.width)} x ${Math.round(box.height)}) in a viewport of ${innerWidth} x ${innerHeight}; under the pointer is ${at ? where(at) : "nothing"}.`;
        }
        """;

    // Runs the action and returns the answer that matches, once it has come. A 401 is skipped: the host's access tokens last
    // five seconds, and the admin sends the call again after a refresh, whose answer is the one that counts; a wait that ends
    // with a 401 seen and no call after it says so. The answer is read by its request, status and headers only: Chromium does
    // not always keep a body for the test to read after the fact.
    public static async Task<IResponse> AnswerAsync(this AdminTab tab, Func<Task> action, Func<IResponse, bool> match)
    {
        var refused = new RefusedCount();
        var from = tab.CallsSeen;
        try
        {
            return await tab.Page.RunAndWaitForResponseAsync(action, refused.Skipping(match), new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        }
        catch (Exception exception) when (exception is TimeoutException || (exception is PlaywrightException && refused.Count > 0))
        {
            throw refused.Explained(exception, tab, from);
        }
    }

    public static Task<IResponse> AnswerAsync(this AdminTab tab, Func<Task> action, string method, string url) =>
        tab.AnswerAsync(action, response => response.Request.Method == method && response.Url == url);

    // An answer waited for from now on, for an action whose call comes later or from another tab; a 401 is skipped as above.
    public static async Task<IResponse> NextAnswerAsync(this AdminTab tab, Func<IResponse, bool> match)
    {
        var refused = new RefusedCount();
        var from = tab.CallsSeen;
        try
        {
            return await tab.Page.WaitForResponseAsync(refused.Skipping(match), new PageWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        }
        catch (Exception exception) when (exception is TimeoutException || (exception is PlaywrightException && refused.Count > 0))
        {
            throw refused.Explained(exception, tab, from);
        }
    }

    // The id of a 201's Location, which names the created item under path, such as "content/article" or "media/folders".
    public static async Task<Guid> CreatedIdAsync(IResponse created, string path)
    {
        created.Status.ShouldBe(201);
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var match = Regex.Match(location, $@"(?:^|/){Regex.Escape(path)}/(?<id>[0-9a-fA-F-]{{36}})(?:\?|$)");
        match.Success.ShouldBeTrue($"the Location of the created item was '{location}', not {path}/{{id}}");
        return Guid.Parse(match.Groups["id"].Value);
    }

    // The dialog as the accessibility tree has it, named by its title: its host element has no box of its own, the library shows
    // it in the top layer. A value inside a dialog says nothing until this is visible.
    public static ILocator ShownDialog(this AdminTab tab, string title) =>
        tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = title, Exact = true });

    // The library's dialog by the title the wrapper sets on its host element, whose controls are its light children.
    public static ILocator Dialog(this AdminTab tab, string title) => tab.Page.Locator($"fluent-dialog[aria-label=\"{title}\"]");

    // Runs the action that opens a dialog and returns the dialog once it is shown.
    public static async Task<ILocator> OpenDialogAsync(this AdminTab tab, Func<Task> open, string title)
    {
        await open();
        await Expect(tab.ShownDialog(title)).ToBeVisibleAsync();
        return tab.Dialog(title);
    }

    // A fluent-button in the scope by its whole text. Its role comes from ElementInternals, which Playwright's role selector
    // does not read.
    public static ILocator ButtonIn(ILocator scope, string text) => scope.Locator("fluent-button").Filter(Exactly(text));

    public static LocatorFilterOptions Exactly(string text) => new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(text)}\s*$") };

    // A state the page reaches on its own, such as a search index that is written a moment after a write: the attempt, which
    // may act again, is made until it holds, within the harness's wait.
    public static async Task UntilAsync(Func<Task<bool>> attempt, string because)
    {
        var deadline = DateTime.UtcNow + AdminBrowser.Wait;
        while (!await attempt())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, because);
            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    // A state that a browser event sets in the test.
    public static Task UntilAsync(Func<bool> condition, string because) => UntilAsync(() => Task.FromResult(condition()), because);

    // A text of the resources with its arguments.
    public static string Format(string text, params string[] arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            text = text.Replace($"{{{index}}}", arguments[index], StringComparison.Ordinal);
        }

        return text;
    }

    private static Task<bool> HasFocusAsync(ILocator target) =>
        target.EvaluateAsync<bool>("element => element === document.activeElement || element.contains(document.activeElement)");

    private static async Task PageTimerAsync(IPage page, int milliseconds) =>
        await page.EvaluateAsync("milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds))", milliseconds);
}

// The 401s a wait skipped, for the message of a wait that found nothing after them.
internal sealed class RefusedCount
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public Func<IResponse, bool> Skipping(Func<IResponse, bool> match) => response =>
    {
        if (!match(response))
        {
            return false;
        }

        if (response.Status == 401)
        {
            Interlocked.Increment(ref _count);
            return false;
        }

        return true;
    };

    // Why the wait ended, with the tab's calls from its start and its last ones before it, which show whether the browser sent
    // the call and saw an answer, and whether the browser stopped reporting the tab's calls at the sign-in's return.
    public Exception Explained(Exception timeout, AdminTab tab, long from)
    {
        var why = Count > 0
            ? $"The call was answered 401 {Count} time(s), and the admin did not send it again after a refresh within {AdminBrowser.Wait.TotalSeconds:0} s."
            : $"No answer that matched came within {AdminBrowser.Wait.TotalSeconds:0} s.";
        if (tab.SilentSinceSignIn() is { } silent)
        {
            why += Environment.NewLine + silent;
        }

        return new TimeoutException(
            string.Join(Environment.NewLine, why, "The tab's calls from the start of the wait:", tab.CallsSince(from), "Its last calls before the wait:", tab.CallsBefore(from)),
            timeout);
    }
}

// The media library's controls, which the library, the detail panel and the picker share.
public static class MediaUi
{
    // A PNG of one pixel.
    public const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public static string Text(string key, params string[] arguments) => Ui.Format(Repository.MediaText(key), arguments);

    public static FilePayload PngFile(string name, string png = Png) => new() { Name = name, MimeType = "image/png", Buffer = Convert.FromBase64String(png) };

    // A visible fluent-button of the page by its whole text.
    public static ILocator Button(AdminTab tab, string key, params string[] arguments) =>
        tab.Page.Locator("fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Text(key, arguments))}\s*$"), Visible = true });

    public static ILocator UploadInput(ILocator scope) => scope.Locator("[data-media-upload] input[type=file]");

    // Uploads into the open folder of the scope, the page or a picker, and returns once every file is stored, so that a read
    // after it sees them; a refused file fails here, by the count.
    public static Task UploadAsync(ILocator scope, params FilePayload[] files) => UploadAsync(scope, 0, files);

    // The same with the number of files that are to be refused, each with its refusal shown.
    public static async Task UploadAsync(ILocator scope, int refused, params FilePayload[] files)
    {
        var done = scope.Locator(".media-upload-done");
        var refusals = scope.Locator(".media-upload-refused");
        var (doneBefore, refusedBefore) = (await done.CountAsync(), await refusals.CountAsync());
        await Expect(UploadInput(scope)).ToBeEnabledAsync();
        await UploadInput(scope).SetInputFilesAsync(files);
        await Expect(refusals).ToHaveCountAsync(refusedBefore + refused);
        await Expect(done).ToHaveCountAsync(doneBefore + files.Length - refused);
    }

    // Searches the scope's library, the page or a picker, and returns once no read of it is under way. The same search again
    // reads nothing, so no answer is waited for; what is asserted next is waited for by its own expectation.
    public static async Task SearchAsync(ILocator scope, string text)
    {
        var box = scope.GetByLabel(Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true });
        await box.FillAsync(text);
        await Expect(box).ToHaveValueAsync(text);
        await Ui.ButtonIn(scope, Text("Media.Search.Submit")).ClickAsync();
        await Expect(scope.Locator("[aria-busy='true']")).ToHaveCountAsync(0);
    }
}
