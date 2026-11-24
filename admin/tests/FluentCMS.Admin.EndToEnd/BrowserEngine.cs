using Microsoft.Playwright;

namespace FluentCMS.Admin.EndToEnd;

// The browser of the run, headless, launched once: Chromium, or Firefox or WebKit when FLUENTCMS_E2E_BROWSER names one.
// Chromium accepts the certificate of the run's host by the hash of its public key and nothing more: every other certificate
// is checked as a browser checks it, no trust store or machine setting is changed, and the host's own https and Secure cookies
// are what the tests see. Firefox and WebKit have no such flag, so their contexts ignore certificate errors; a context visits
// the run's own servers on the loopback address and the preview's front end, which the test answers itself (spec 7). Where
// the browser is not installed, as on a developer's machine that has not run the install, the browser tests are skipped with
// the reason; CI installs it.
internal sealed class BrowserEngine
{
    public const string Variable = "FLUENTCMS_E2E_BROWSER";

    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromMinutes(1);

    private BrowserEngine(string name, string display) => (Name, Display) = (name, display);

    public static BrowserEngine Chromium { get; } = new("chromium", "Chromium");

    public static BrowserEngine Firefox { get; } = new("firefox", "Firefox");

    public static BrowserEngine WebKit { get; } = new("webkit", "WebKit");

    // chromium, firefox or webkit, as Playwright's install names it.
    public string Name { get; }

    public string Display { get; }

    public bool IsChromium => this == Chromium;

    // The browser FLUENTCMS_E2E_BROWSER names, or Chromium when it is not set.
    public static BrowserEngine Chosen()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        return string.IsNullOrWhiteSpace(value) ? Chromium
            : new[] { Chromium, Firefox, WebKit }.FirstOrDefault(engine => string.Equals(engine.Name, value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{Variable} is '{value}'; set it to chromium, firefox or webkit, or leave it unset for Chromium.");
    }

    public BrowserNewContextOptions ContextOptions() => new()
    {
        Locale = "en-US",
        ServiceWorkers = ServiceWorkerPolicy.Block,
        IgnoreHTTPSErrors = IsChromium ? null : true,
    };

    // What a page needs to write and read the clipboard as a user who allows it. WebKit knows no clipboard-write and lets a
    // click write; Firefox knows neither and lets a page of the run read what it wrote.
    public string[] ClipboardPermissions =>
        IsChromium ? ["clipboard-read", "clipboard-write"] : this == WebKit ? ["clipboard-read"] : [];

    public async Task<(IPlaywright? Playwright, IBrowser? Browser, string? Missing)> LaunchAsync(string serverKeyHash)
    {
        var started = DateTime.UtcNow;
        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await TypeOf(playwright).LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = (float)LaunchTimeout.TotalMilliseconds,
                Args = IsChromium ? [$"--ignore-certificate-errors-spki-list={serverKeyHash}"] : null,
            });
            return (playwright, browser, null);
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            playwright.Dispose();
            DeleteEmptyFolders(started);
            var script = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
            return (null, null, IsChromium
                ? "Chromium for Playwright is not installed, so the browser tests are skipped; they run in full CI. To run them here, "
                    + $"run: pwsh {script} install chromium (CONTRIBUTING.md)."
                : $"{Display} for Playwright is not installed, so the browser tests are skipped; they run in the nightly CI run. To run "
                    + $"them here, run: pwsh {script} install {Name} (CONTRIBUTING.md).");
        }
    }

    // Whether Playwright's browsers folder holds this browser, where the launch looks (PLAYWRIGHT_BROWSERS_PATH, or the user's
    // cache), for work worth starting before the launch; only the launch tells for sure.
    public bool LooksInstalled()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } set && set != "0" ? set
            : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright")
            : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Caches", "ms-playwright")
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } cache ? cache : Path.Combine(home, ".cache"), "ms-playwright");
        return Directory.Exists(folder) && Directory.EnumerateDirectories(folder, Name + "*").Any();
    }

    private IBrowserType TypeOf(IPlaywright playwright) => IsChromium ? playwright.Chromium : this == Firefox ? playwright.Firefox : playwright.Webkit;

    // The folders a launch makes in the temporary directory, which stay empty when there was no browser to launch.
    private static void DeleteEmptyFolders(DateTime since)
    {
        foreach (var folder in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories("playwright*"))
        {
            try
            {
                if (folder.CreationTimeUtc >= since.AddSeconds(-1) && !folder.EnumerateFileSystemInfos().Any())
                {
                    folder.Delete();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
