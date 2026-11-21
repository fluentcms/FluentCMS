using Bunit.TestDoubles;
using Shouldly;

namespace FluentCMS.Admin.Tests;

// bUnit's navigation manager ends a navigation that a location-changing handler held, as a question before leaving does, on
// the thread that completed the handler and not on the renderer's dispatcher: it records the navigation, sets the address
// and raises LocationChanged there, after the last render. A wait on renders can miss that end, and the router, whose render
// off the dispatcher fails unseen, does not show the new route. So a test of such a navigation waits for the record, which
// holds each navigation once it has ended, and asserts the address and the record, not the page the router shows.
internal static class NavigationEnd
{
    public static async Task EndedAsync(this BunitNavigationManager navigation, int ended, NavigationState state)
    {
        var deadline = DateTime.UtcNow + FluentCMS.Admin.Testing.ComponentTestSetup.WaitTimeout;
        while (navigation.History.Count <= ended)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the navigation did not end {state}");
            await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
        }

        navigation.History.First().State.ShouldBe(state);
    }
}
