namespace FluentCMS.IntegrationTests.Browser;

public sealed class SmokeTests
{
    [BrowserFact]
    public async Task Signing_in_by_keyboard_lands_on_the_desk_with_the_admin_script()
    {
        await using var session = await BrowserSession.StartAsync();

        await session.SignInWithKeyboardAsync();

        Assert.Equal("Desk", await session.Page.Locator("h1").TextContentAsync());
        var script = await session.Page.EvaluateAsync<int>("fetch(document.querySelector('script[type=module]').src).then(r => r.text()).then(t => t.length)");
        Assert.True(script > 1000, $"admin.js is {script} characters.");
    }
}
