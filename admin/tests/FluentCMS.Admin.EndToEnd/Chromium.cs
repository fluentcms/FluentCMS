using Microsoft.Playwright;

namespace FluentCMS.Admin.EndToEnd;

// Chromium, headless, launched once for the run. It accepts the certificate of the run's host by the hash of its public key
// and nothing more: every other certificate is checked as a browser checks it, no trust store or machine setting is
// changed, and the host's own https and Secure cookies are what the tests see. Where the browser is not installed, as on a developer's
// machine that has not run the install, the browser tests are skipped with the reason; full CI installs it.
internal static class Chromium
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromMinutes(1);

    public static async Task<(IPlaywright? Playwright, IBrowser? Browser, string? Missing)> LaunchAsync(string serverKeyHash)
    {
        var started = DateTime.UtcNow;
        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = (float)LaunchTimeout.TotalMilliseconds,
                Args = [$"--ignore-certificate-errors-spki-list={serverKeyHash}"],
            });
            return (playwright, browser, null);
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            playwright.Dispose();
            DeleteEmptyFolders(started);
            return (null, null,
                "Chromium for Playwright is not installed, so the browser tests are skipped; they run in full CI. To run them here, "
                + $"run: pwsh {Path.Combine(AppContext.BaseDirectory, "playwright.ps1")} install chromium (CONTRIBUTING.md).");
        }
    }

    // Whether Playwright's browsers folder holds a Chromium, where the launch looks (PLAYWRIGHT_BROWSERS_PATH, or the user's
    // cache), for work worth starting before the launch; only the launch tells for sure.
    public static bool LooksInstalled()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } set && set != "0" ? set
            : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright")
            : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Caches", "ms-playwright")
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } cache ? cache : Path.Combine(home, ".cache"), "ms-playwright");
        return Directory.Exists(folder) && Directory.EnumerateDirectories(folder, "chromium*").Any();
    }

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
