using System.Net;
using System.Text.Json;

namespace FluentCMS.Client;

// A problem (RFC 9457) the API answered, with its FCMS code. A response that is not a problem has no code.
public sealed class FluentCmsProblemException : Exception
{
    public FluentCmsProblemException()
    {
    }

    public FluentCmsProblemException(string message)
        : base(message)
    {
    }

    public FluentCmsProblemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FluentCmsProblemException(HttpStatusCode status, string? code, string? title, string? detail, IReadOnlyList<ProblemError> errors, string? traceId)
        : base($"{code ?? ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)}: {detail ?? title ?? status.ToString()}")
    {
        Status = status;
        Code = code;
        Title = title;
        Detail = detail;
        Errors = errors;
        TraceId = traceId;
    }

    public HttpStatusCode Status { get; }

    public string? Code { get; }

    public string? Title { get; }

    public string? Detail { get; }

    public IReadOnlyList<ProblemError> Errors { get; } = [];

    public string? TraceId { get; }

    internal static FluentCmsProblemException From(HttpStatusCode status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                List<ProblemError> errors = [];
                if (root.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    errors.AddRange(list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item => new ProblemError(Text(item, "path"), Text(item, "code"), Text(item, "message"))));
                }

                return new FluentCmsProblemException(status, Text(root, "code"), Text(root, "title"), Text(root, "detail"), errors, Text(root, "traceId"));
            }
        }
        catch (JsonException)
        {
        }

        return new FluentCmsProblemException(status, null, null, null, [], null);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed record ProblemError(string? Path, string? Code, string? Message);
