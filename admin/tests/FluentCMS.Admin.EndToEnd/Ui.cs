using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The one copy of what the browser tests do the same way. It is the only file of the tests that waits by a timer, and the
// architecture tests hold the test classes to it (spec 7).
public static class Ui
{
    // Past the component library's second focus move, 25 ms after a dialog closes (FocusOnPreviousActiveElement), and the
    // admin's own, 100 ms after it (DocumentInterop.DialogFocusDelay). A timer of the page set now runs after both, since
    // timers run in the order they expire.
    private const int FocusSettle = 150;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // Focus as Tab would give it. A control in a dialog that is not shown yet cannot take focus, and the dialog does not give
    // it to the control as it opens, so focus is asked for once the control shows, and again until the control has it.
    public static async Task FocusAsync(ILocator target)
    {
        await Expect(target).ToBeVisibleAsync();
        var deadline = DateTime.UtcNow + AdminBrowser.Wait;
        while (true)
        {
            await target.FocusAsync();
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement || element.contains(document.activeElement)") || DateTime.UtcNow > deadline)
            {
                break;
            }

            await PageTimerAsync(target.Page, 50);
        }

        await Expect(target).ToBeFocusedAsync();
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
    // five seconds, and the admin sends the call again after a refresh, whose answer is the one that counts. The answer is
    // read by its request, status and headers only: Chromium does not always keep a body for the test to read after the fact.
    public static Task<IResponse> AnswerAsync(this AdminTab tab, Func<Task> action, Func<IResponse, bool> match) =>
        tab.Page.RunAndWaitForResponseAsync(action, response => response.Status != 401 && match(response), new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });

    public static Task<IResponse> AnswerAsync(this AdminTab tab, Func<Task> action, string method, string url) =>
        tab.AnswerAsync(action, response => response.Request.Method == method && response.Url == url);

    // An answer waited for from now on, for an action whose call comes later or from another tab; a 401 is skipped as above.
    public static Task<IResponse> NextAnswerAsync(this AdminTab tab, Func<IResponse, bool> match) =>
        tab.Page.WaitForResponseAsync(response => response.Status != 401 && match(response), new PageWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });

    // The id at the end of a 201's Location.
    public static async Task<Guid> CreatedIdAsync(IResponse created)
    {
        created.Status.ShouldBe(201);
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        return Guid.Parse(location[(location.LastIndexOf('/') + 1)..].Split('?')[0]);
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

    private static async Task PageTimerAsync(IPage page, int milliseconds) =>
        await page.EvaluateAsync("milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds))", milliseconds);
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

    // Uploads into the open folder of the scope, the page or a picker, and returns once each file has a state, done or refused,
    // so that a read after it sees what was stored.
    public static async Task UploadAsync(ILocator scope, params FilePayload[] files)
    {
        var states = scope.Locator(".media-upload-done, .media-upload-refused");
        var before = await states.CountAsync();
        await Expect(UploadInput(scope)).ToBeEnabledAsync();
        await UploadInput(scope).SetInputFilesAsync(files);
        await Expect(states).ToHaveCountAsync(before + files.Length);
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
