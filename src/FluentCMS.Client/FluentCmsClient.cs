using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Client;

// The Delivery reads and the content group of the Management API over the caller's HttpClient, whose BaseAddress is the
// instance and which carries the key or the bearer token. A type is named by its content type or, for a generated class,
// by its ContentType attribute. An answer that is not a success throws FluentCmsProblemException.
public sealed class FluentCmsClient(HttpClient http)
{
    private const string Delivery = "api/delivery/v1/";
    private const string Management = "api/management/v1/content/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public Task<EntryPage<T>> ListAsync<T>(ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry => ListAsync<T>(TypeOf<T>(), query, cancellationToken);

    public async Task<EntryPage<T>> ListAsync<T>(string type, ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<EntryPage<T>>(HttpMethod.Get, Delivery + Segment(type), query, null, null, cancellationToken))!;

    public Task<T> GetAsync<T>(Guid id, ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry => GetAsync<T>(TypeOf<T>(), id, query, cancellationToken);

    public async Task<T> GetAsync<T>(string type, Guid id, ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<T>(HttpMethod.Get, $"{Delivery}{Segment(type)}/{id}", query, null, null, cancellationToken))!;

    // By a unique field, such as a slug.
    public Task<T> GetByAsync<T>(string field, string value, ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry => GetByAsync<T>(TypeOf<T>(), field, value, query, cancellationToken);

    public async Task<T> GetByAsync<T>(string type, string field, string value, ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<T>(HttpMethod.Get, $"{Delivery}{Segment(type)}/by/{Segment(field)}/{Segment(value)}", query, null, null, cancellationToken))!;

    // A singleton's list route answers its one entry.
    public Task<T> GetSingletonAsync<T>(ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry => GetSingletonAsync<T>(TypeOf<T>(), query, cancellationToken);

    public async Task<T> GetSingletonAsync<T>(string type, ContentQuery? query = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<T>(HttpMethod.Get, Delivery + Segment(type), query, null, null, cancellationToken))!;

    public Task<T> CreateAsync<T>(T entry, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry => CreateAsync(TypeOf<T>(), entry, options, cancellationToken);

    public async Task<T> CreateAsync<T>(string type, T entry, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<T>(HttpMethod.Post, Management + Segment(type), Locale(options), Body(entry), null, cancellationToken))!;

    // Replaces the draft at the version read: a field left out is cleared, and another version is a conflict (412). A class
    // generated from an older schema leaves out the fields it does not know, so it clears them; generate it again first.
    public Task<T> UpdateAsync<T>(Guid id, T entry, int version, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry => UpdateAsync(TypeOf<T>(), id, entry, version, options, cancellationToken);

    public async Task<T> UpdateAsync<T>(string type, Guid id, T entry, int version, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<T>(HttpMethod.Put, $"{Management}{Segment(type)}/{id}", Locale(options), Body(entry), version, cancellationToken))!;

    // Moves the entry to the trash, in every locale.
    public Task DeleteAsync<T>(Guid id, CancellationToken cancellationToken = default)
        where T : Entry => DeleteAsync(TypeOf<T>(), id, cancellationToken);

    public Task DeleteAsync(string type, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement?>(HttpMethod.Delete, $"{Management}{Segment(type)}/{id}", null, null, null, cancellationToken);

    public Task<PublishResult<T>> PublishAsync<T>(Guid id, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry => PublishAsync<T>(TypeOf<T>(), id, options, cancellationToken);

    public async Task<PublishResult<T>> PublishAsync<T>(string type, Guid id, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<PublishResult<T>>(HttpMethod.Post, $"{Management}{Segment(type)}/{id}/publish", Locale(options), null, null, cancellationToken))!;

    public Task<T> UnpublishAsync<T>(Guid id, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry => UnpublishAsync<T>(TypeOf<T>(), id, options, cancellationToken);

    public async Task<T> UnpublishAsync<T>(string type, Guid id, WriteOptions? options = null, CancellationToken cancellationToken = default)
        where T : Entry =>
        (await SendAsync<T>(HttpMethod.Post, $"{Management}{Segment(type)}/{id}/unpublish", Locale(options), null, null, cancellationToken))!;

    private async Task<TResult?> SendAsync<TResult>(HttpMethod method, string path, ContentQuery? query, JsonObject? body, int? version, CancellationToken cancellationToken)
    {
        var queryString = query?.ToQueryString() ?? "";
        var relative = new Uri(queryString.Length == 0 ? path : $"{path}?{queryString}", UriKind.Relative);

        // The routes are below the base address, so https://host/cms reaches https://host/cms/api/..., as with a slash.
        using var request = new HttpRequestMessage(method, _http.BaseAddress is { } baseAddress ? new Uri(Directory(baseAddress), relative) : relative);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json");
        }

        if (version is { } expected)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expected.ToString(CultureInfo.InvariantCulture)}\""));
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw FluentCmsProblemException.From(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        if (response.Content.Headers.ContentLength == 0 || response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResult>(stream, Json, cancellationToken);
    }

    // The fields of the entry; the system fields are the server's.
    private static JsonObject Body<T>(T entry)
        where T : Entry
    {
        ArgumentNullException.ThrowIfNull(entry);

        var body = JsonSerializer.SerializeToNode(entry, entry.GetType(), Json) as JsonObject ?? [];
        foreach (var name in Entry.SystemFields)
        {
            body.Remove(name);
        }

        return body;
    }

    private static Uri Directory(Uri baseAddress) =>
        baseAddress.AbsolutePath.EndsWith('/') ? baseAddress : new UriBuilder(baseAddress) { Path = baseAddress.AbsolutePath + "/" }.Uri;

    private static ContentQuery? Locale(WriteOptions? options) => options?.Locale is { } locale ? new ContentQuery { Locale = locale } : null;

    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return Uri.EscapeDataString(value);
    }

    private static string TypeOf<T>() =>
        typeof(T).GetCustomAttribute<ContentTypeAttribute>()?.Name
        ?? throw new InvalidOperationException($"{typeof(T).Name} has no ContentType attribute; name the content type.");
}
