using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Cli;

// The transfer endpoints of a running instance (spec 4.14), with the management API key in the Authorization header. The
// key goes nowhere else, and neither it nor the address is written out with a refusal.
internal sealed class TransferClient : IDisposable
{
    public const string SiteHeader = "X-Cms-Site";

    private const string Transfer = "api/management/v1/transfer/";

    private readonly HttpClient _http;
    private readonly Uri _url;

    // The client is the tool's for this command, which this disposes.
    public TransferClient(Instance instance, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(http);

        _url = instance.Url;
        _http = http;

        // A path base such as /cms stays in front of the endpoints' paths.
        _http.BaseAddress = instance.Url.AbsolutePath.EndsWith('/') ? instance.Url : new Uri(instance.Url.AbsoluteUri + "/");

        // An export or an import takes as long as the package needs; Ctrl+C stops it.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", instance.Token);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("fluentcms", Version));
    }

    public void Dispose() => _http.Dispose();

    // The package as the endpoint writes it, into a file beside the target that is moved over it once complete.
    public async Task ExportAsync(JsonObject body, string? site, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Transfer + "export", UriKind.Relative))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddSite(request, site);
        using var response = await SendAsync(request, cancellationToken);

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await Reading(() => response.Content.CopyToAsync(file, cancellationToken));
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Sends the package and hands each progress line on; returns the last line, the report, as the endpoint wrote it.
    public async Task<string> ImportAsync(string path, string? site, string query, Func<JsonElement, Task> progress, CancellationToken cancellationToken)
    {
        FileStream package;
        try
        {
            package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(CliErrorCodes.TransferFileNotRead, $"{path} could not be read: {exception.Message}");
        }

        await using (package)
        {
            using var content = new StreamContent(package, 81920);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Transfer + "import" + query, UriKind.Relative)) { Content = content };
            AddSite(request, site);
            using var response = await SendAsync(request, cancellationToken);

            string? report = null;
            await Reading(async () =>
            {
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken), Encoding.UTF8);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    using var json = Parse(line);
                    if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("report", out _))
                    {
                        report = line;
                    }
                    else
                    {
                        await progress(json.RootElement);
                    }
                }
            });

            return report ?? throw new CliException(CliErrorCodes.TransferAnswerUnexpected, "the instance's answer ended before its report, so the import may not have finished; running it again finishes it.");
        }
    }

    private static string Version =>
        (typeof(TransferClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    private static void AddSite(HttpRequestMessage request, string? site)
    {
        if (site is not null)
        {
            request.Headers.Add(SiteHeader, site);
        }
    }

    private static JsonDocument Parse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            throw new CliException(CliErrorCodes.TransferAnswerUnexpected, "the instance answered a line that is not JSON; is the address that of a FluentCMS instance?");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await RefusedAsync(response, cancellationToken);
        }
    }

    // A connection that breaks while the answer is read.
    private async Task Reading(Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpIOException)
        {
            throw Unreachable(exception);
        }
    }

    private CliException Unreachable(Exception exception) =>
        new(CliErrorCodes.TransferUnreachable, $"{_url.GetLeftPart(UriPartial.Authority)} could not be reached or stopped answering: {exception.Message}");

    // A problem of the endpoint keeps its own code, such as FCMS3202 for a schema the instance cannot hold, so the message
    // is the one the command gives inside the host.
    private static async Task<CliException> RefusedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        string? code = null;
        string? message = null;
        var errors = new List<string>();
        try
        {
            using var problem = JsonDocument.Parse(text);
            var root = problem.RootElement;
            code = String(root, "code");
            message = (String(root, "detail") ?? String(root, "title"))?.TrimEnd('.');
            if (root.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in list.EnumerateArray())
                {
                    var path = String(error, "path");
                    errors.Add($"{String(error, "code")}: {(string.IsNullOrEmpty(path) ? "" : $"{path}: ")}{String(error, "message")}");
                }
            }
        }
        catch (JsonException)
        {
        }

        var hint = status switch
        {
            401 => "; check the key in --token or FLUENTCMS_TOKEN",
            403 => "; the key needs transfer.manage in the site",
            _ => "",
        };
        var lines = string.Concat(errors.Select(error => Environment.NewLine + error));
        return code is not null && code.StartsWith("FCMS", StringComparison.Ordinal)
            ? new CliException(code, $"{message ?? $"the instance answered {status}"}{hint}{lines}")
            : new CliException(CliErrorCodes.TransferRefused, $"the instance answered {status} {response.ReasonPhrase}{(message is null ? "" : $": {message}")}{hint}{lines}");
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
