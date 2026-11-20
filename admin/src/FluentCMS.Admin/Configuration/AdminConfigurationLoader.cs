using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace FluentCMS.Admin.Configuration;

internal static class AdminConfigurationLoader
{
    public const string FileName = "config.json";

    // The page is blank until the file is read, so a host that does not answer ends in the error page instead.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // The client's base address is the app's own base URL. The browser's HttpClient hands the request option to fetch as
    // cache: "no-store", so a changed file is read on the next start and never cached.
    public static async Task<AdminStartup> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        using var request = new HttpRequestMessage(HttpMethod.Get, FileName);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);

        string json;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Unreadable((int)response.StatusCode);
            }

            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Unreadable(null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's timeout, not a cancellation by the caller.
            return Unreadable(null);
        }
        catch (InvalidOperationException)
        {
            // A charset in the Content-Type that cannot be decoded.
            return Unreadable(null);
        }

        return AdminConfigurationReader.Read(json, http.BaseAddress?.GetLeftPart(UriPartial.Authority));
    }

    private static AdminStartup Unreadable(int? statusCode) =>
        new([new ConfigurationProblem(ConfigurationProblemKind.Unreadable, StatusCode: statusCode)], []);
}
