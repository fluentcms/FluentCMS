using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

// The host on a free port with a headless Chromium page signed in as the owner, the way a reviewer starts the day.
internal sealed class BrowserSession : IAsyncDisposable
{
    private readonly TemporaryInstall _install;
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private BrowserSession(TemporaryInstall install, AdminHost host, Guid ownerId, IPlaywright playwright, IBrowser browser, IPage page)
    {
        _install = install;
        Host = host;
        OwnerId = ownerId;
        _playwright = playwright;
        _browser = browser;
        Page = page;
    }

    public AdminHost Host { get; }

    public Guid OwnerId { get; }

    public IPage Page { get; }

    public string BaseUrl => Host.Client.BaseAddress!.ToString().TrimEnd('/');

    // Settings such as a TemporaryDatabase's configuration run the host on another provider.
    public static async Task<BrowserSession> StartAsync(IDictionary<string, string?>? settings = null)
    {
        var install = new TemporaryInstall();
        var host = await AdminHost.StartAsync(install, settings: settings);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);

        return new BrowserSession(install, host, ownerId, playwright, browser, page);
    }

    // Signs in with the keyboard alone: type into the focused fields and press Enter.
    public async Task SignInWithKeyboardAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/sign-in");
        await Page.GetByLabel("Email").FocusAsync();
        await Page.Keyboard.TypeAsync(AdminHost.Owner.Email);
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.TypeAsync(AdminHost.Owner.Password);
        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForURLAsync($"{BaseUrl}/");
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        await Host.DisposeAsync();
        _install.Dispose();
    }
}
