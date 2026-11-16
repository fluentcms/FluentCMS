using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FluentCMS.Admin.Api;

/// <summary>
/// The admin's one pipeline to the Management API. It sends the caller's token, the shell's site and the view's content
/// locale, waits once on a 429, and turns every answer that is not a success into an <see cref="ApiProblemException"/>.
/// </summary>
/// <remarks>
/// When there is no answer to map, the call throws what <see cref="HttpClient"/> throws: <see cref="HttpRequestException"/>
/// for a network failure, <see cref="TaskCanceledException"/> for a timeout or <see cref="OperationCanceledException"/> for
/// a cancellation. A success whose body is not the JSON of the record, such as the <c>index.html</c> a single-page fallback
/// answers for a wrong API address, throws <see cref="JsonException"/>.
/// </remarks>
public interface IManagementApi
{
    /// <summary>Sends a request and reads its answer through the source-generated <paramref name="answer"/>.</summary>
    /// <typeparam name="T">The record the answer is read into.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="answer">How the answer is read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The answer, with its status and entity tag.</returns>
    /// <exception cref="ApiProblemException">The API did not answer with a success.</exception>
    /// <exception cref="ArgumentException">The path leads out of the Management API, or the request names a site or locale and also says it sends none.</exception>
    /// <exception cref="JsonException">The API answered with a success that is not the JSON of <typeparamref name="T"/>.</exception>
    Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken);

    /// <summary>Sends a request whose answer has no body the caller reads, such as a 204.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The status and entity tag of the answer.</returns>
    /// <exception cref="ApiProblemException">The API did not answer with a success.</exception>
    /// <exception cref="ArgumentException">The path leads out of the Management API, or the request names a site or locale and also says it sends none.</exception>
    Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken);
}
