using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS.Cli;

// The Management API and /mcp of a running instance, for the commands that change it, with the management API key in the
// ApiKey scheme. A refusal of the API becomes its problem, with the API's code first, and exit code 1. The key is never
// written out. It owns the HttpClient it is given.
internal sealed class ManagementClient : IDisposable
{
    public const string ManagementPrefix = "api/management/v1/";

    private readonly HttpClient _http;
    private readonly Uri _base;

    // A timeout of null keeps HttpClient's; the MCP proxy waits as long as a tool call takes.
    public ManagementClient(Instance instance, HttpClient http, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(http);

        // A header cannot carry it, and HttpClient would refuse it with a message that repeats it.
        if (instance.Token.Any(character => character is < '!' or > '~'))
        {
            http.Dispose();
            throw new CliException(CliErrorCodes.InvalidInstance, "the key holds a character that no API key has; the value is not repeated here.", ExitCodes.BadUsage);
        }

        Instance = instance;
        _http = http;
        if (timeout is { } wait)
        {
            _http.Timeout = wait;
        }

        _base = new Uri(instance.Url.AbsoluteUri.EndsWith('/') ? instance.Url.AbsoluteUri : instance.Url.AbsoluteUri + "/");
    }

    public Instance Instance { get; }

    // An option left out is left out of the body too, so that the API applies its default.
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public Uri Address(string path) => new(_base, path);

    public HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, Address(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", Instance.Token);
        return request;
    }

    // A call of the Management API; null for an answer with no body.
    public async Task<JsonElement?> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = Request(method, ManagementPrefix + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw Problem(response, text);
        }

        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new CliException(CliErrorCodes.UnexpectedAnswer, $"{Instance} answered {method} {path} with something that is not JSON; is it a FluentCMS instance?");
        }
    }

    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Get, path, null, cancellationToken)
            ?? throw new CliException(CliErrorCodes.UnexpectedAnswer, $"{Instance} answered GET {path} with no body.");

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _http.SendAsync(request, completion, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException(CliErrorCodes.InstanceUnreachable, $"{Instance} could not be reached: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliException(CliErrorCodes.InstanceUnreachable, $"{Instance} did not answer in time.");
        }
    }

    // The API's problem: its code, title and detail, and the errors of a validation problem, one per line.
    public static CliException Problem(HttpResponseMessage response, string body)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (InstanceAnswer.IsRedirect(response))
        {
            return new CliException(CliErrorCodes.UnexpectedAnswer, $"the instance answered {InstanceAnswer.Redirect(response)}.");
        }

        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        try
        {
            using var json = JsonDocument.Parse(body);
            var problem = json.RootElement;
            if (problem.ValueKind == JsonValueKind.Object && Text(problem, "code") is { } code)
            {
                var message = new StringBuilder(Text(problem, "detail") ?? Text(problem, "title") ?? status);
                if (problem.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var error in errors.EnumerateArray().Where(error => error.ValueKind == JsonValueKind.Object))
                    {
                        message.Append(Environment.NewLine).Append(CultureInvariant($"  {Text(error, "code")}: {Text(error, "path")}: {Text(error, "message")}"));
                    }
                }

                return new CliException(code, message.ToString());
            }
        }
        catch (JsonException)
        {
        }

        return new CliException(CliErrorCodes.UnexpectedAnswer, $"the instance answered {status} with no problem of FluentCMS.");
    }

    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public void Dispose() => _http.Dispose();

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);
}
