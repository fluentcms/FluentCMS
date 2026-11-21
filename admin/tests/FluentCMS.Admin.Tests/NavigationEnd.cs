using Bunit.TestDoubles;
using Shouldly;

namespace FluentCMS.Admin.Tests;

// bUnit's navigation manager ends a navigation that a location-changing handler held, as a question before leaving does, on
// the thread that completed the handler and not on the renderer's dispatcher: it records the navigation, sets the address
// and raises LocationChanged there, after the last render. A wait on renders can miss that end, and the router, whose render
// off the dispatcher fails unseen, does not show the new route. So a test of such a navigation waits for the record, which
// holds each navigation once it has ended, and asserts the record, not the address or the page the router shows.
internal static class NavigationEnd
{
    // Returns the record of the navigation that ended last, once more than `ended` have.
    public static async Task<NavigationHistory> EndedAsync(this BunitNavigationManager navigation, int ended, NavigationState state)
    {
        var deadline = DateTime.UtcNow + FluentCMS.Admin.Testing.ComponentTestSetup.WaitTimeout;
        NavigationHistory[] records;
        while ((records = navigation.Records()).Length <= ended)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the navigation did not end {state}");
            await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
        }

        records[0].State.ShouldBe(state);
        return records[0];
    }

    // The records, newest first. bUnit keeps them in a plain stack that it pushes to on another thread, so the copy enumerates
    // it, which reports a push during the copy as a modification, and a copy that meets one is taken again.
    public static NavigationHistory[] Records(this BunitNavigationManager navigation)
    {
        var deadline = DateTime.UtcNow + FluentCMS.Admin.Testing.ComponentTestSetup.WaitTimeout;
        while (true)
        {
            try
            {
                return Enumerable.ToArray(navigation.History);
            }
            catch (InvalidOperationException) when (DateTime.UtcNow < deadline)
            {
            }
        }
    }
}
