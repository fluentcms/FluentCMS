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

    // Runs the action and returns the answer that matches, once it has come. A 401 is skipped: the host's access tokens last
    // five seconds, and the admin sends the call again after a refresh, whose answer is the one that counts; a wait that ends
    // with a 401 seen and no call after it says so. The answer is read by its request, status and headers only: Chromium does
    // not always keep a body for the test to read after the fact.
    public static async Task<IResponse> AnswerAsync(this AdminTab tab, Func<Task> action, Func<IResponse, bool> match)
    {
        var refused = new RefusedCount();
        try
        {
            return await tab.Page.RunAndWaitForResponseAsync(action, refused.Skipping(match), new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        }
        catch (Exception exception) when (exception is TimeoutException or PlaywrightException && refused.Count > 0)
        {
            throw refused.Explained(exception);
        }
    }

    public static Task<IResponse> AnswerAsync(this AdminTab tab, Func<Task> action, string method, string url) =>
        tab.AnswerAsync(action, response => response.Request.Method == method && response.Url == url);

    // An answer waited for from now on, for an action whose call comes later or from another tab; a 401 is skipped as above.
    public static async Task<IResponse> NextAnswerAsync(this AdminTab tab, Func<IResponse, bool> match)
    {
        var refused = new RefusedCount();
        try
        {
            return await tab.Page.WaitForResponseAsync(refused.Skipping(match), new PageWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        }
        catch (Exception exception) when (exception is TimeoutException or PlaywrightException && refused.Count > 0)
        {
            throw refused.Explained(exception);
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

    public Exception Explained(Exception timeout) =>
        new TimeoutException($"The call was answered 401 {Count} time(s), and the admin did not send it again after a refresh within {AdminBrowser.Wait.TotalSeconds:0} s.", timeout);
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
