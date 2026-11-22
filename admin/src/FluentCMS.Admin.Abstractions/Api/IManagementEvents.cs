namespace FluentCMS.Admin.Api;

/// <summary>
/// The admin's pipeline to the Management API, as <see cref="IManagementApi"/>, for a route that answers server-sent events
/// (<c>text/event-stream</c>), such as an AI action: the request sends the caller's token, the shell's site and the view's
/// content locale, and its events are read as they arrive.
/// </summary>
/// <remarks>
/// An answer that is not a success throws <see cref="ApiProblemException"/> before the first event, and is not sent again, a
/// 429 included, whatever its <c>Retry-After</c>: the caller shows the problem. A success that is not an event stream throws
/// <see cref="System.Text.Json.JsonException"/>. When there is no answer, the call throws what <see cref="HttpClient"/> throws.
/// </remarks>
public interface IManagementEvents
{
    /// <summary>Sends a request that accepts <c>text/event-stream</c> and reads its events as they arrive.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the request and the read of its events.</param>
    /// <returns>The events, in the order the server sent them, until the server ends the answer.</returns>
    /// <exception cref="ApiProblemException">The API did not answer with a success.</exception>
    /// <exception cref="ArgumentException">The path leads out of the Management API, or the request names a site or locale and also says it sends none.</exception>
    IAsyncEnumerable<ApiEvent> ReadAsync(ApiRequest request, CancellationToken cancellationToken);
}

/// <summary>One server-sent event: its name and its data.</summary>
public sealed class ApiEvent
{
    /// <summary>Creates an event.</summary>
    /// <param name="name">The name of the event, <c>message</c> when the server named none.</param>
    /// <param name="data">The data of the event, its lines joined with a line feed.</param>
    public ApiEvent(string name, string data)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(data);
        Name = name;
        Data = data;
    }

    /// <summary>Gets the name of the event.</summary>
    public string Name { get; }

    /// <summary>Gets the data of the event.</summary>
    public string Data { get; }
}
