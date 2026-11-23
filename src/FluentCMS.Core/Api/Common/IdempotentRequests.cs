using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Api.Common;

// Runs a write that may carry an Idempotency-Key. A request without one runs as it is. With one, the request is hashed
// with its method, path, query, locale, content type, body and principal, the key is claimed, and the response is kept
// for a repeat. Only a response below 500 is kept; a refused request, which throws, and a failure release the key, so
// that a repeat runs again. The lease of the key is renewed while the request runs, however long it takes, and a request
// whose key a repeat took over all the same, after renewals failed for a whole lease, is cancelled; one whose work answered
// first answers what its work answered and keeps nothing. A key removed under its request, past its window, is put back.
// The answer of work that answered is kept also when the client went away meanwhile, so a retry is answered it.
internal static class IdempotentRequests
{
    public const string ReplayedHeader = "Idempotent-Replayed";

    private static readonly string[] KeptHeaders = [HeaderNames.ContentType, HeaderNames.Location, HeaderNames.ETag];

    public static async Task<IResult> RunAsync(HttpContext httpContext, Func<Task<IResult>> handler)
    {
        var values = httpContext.Request.Headers[IdempotencyStore.Header];
        if (values.Count == 0)
        {
            return await handler();
        }

        var key = IdempotencyStore.Key(values);
        var store = httpContext.RequestServices.GetRequiredService<IdempotencyStore>();
        var api = ApiRequest.Of(httpContext);
        var hash = await HashAsync(httpContext, api);
        var start = await store.BeginAsync(api.Operation, key, hash, httpContext.RequestAborted);
        if (start.Stored is { } stored)
        {
            return new Replay(stored);
        }

        var claim = start.Claim!;
        var aborted = httpContext.RequestAborted;
        var lease = store.Hold(claim);
        var lost = lease.Lost;
        StoredResponse response;
        try
        {
            try
            {
                IResult result;

                // The work stops when the client goes away, and when a repeat took the key over, so that it never runs beside it.
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted, lost))
                {
                    httpContext.RequestAborted = stop.Token;
                    try
                    {
                        result = await handler();
                    }
                    finally
                    {
                        httpContext.RequestAborted = aborted;
                    }
                }

                // The work has answered, so what it committed stands: a key lost from here on no longer stops the answer, and
                // neither does the client going away, so that a retry is answered what the write answered (#3485).
                response = await CaptureAsync(httpContext, result, store, claim, aborted);
            }
            finally
            {
                await lease.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The client went away before the work answered, perhaps after it committed, or the capture of its answer ran past
            // its bound: the key stays held until its lease runs out, which is renewed for as long as the work ran, so that a
            // retry never runs it a second time while the first may have succeeded or may still be running.
            throw;
        }
        catch (OperationCanceledException) when (lost.IsCancellationRequested)
        {
            // The key is the repeat's now, so it is not this request's to free.
            throw IdempotencyStore.TakenOver();
        }
        catch
        {
            await store.ReleaseAsync(claim);
            throw;
        }

        // The client gets what its work answered either way, but a key a repeat took is neither kept nor freed by it.
        if (!lost.IsCancellationRequested)
        {
            if (response.Status < StatusCodes.Status500InternalServerError)
            {
                await store.CompleteAsync(claim, response);
            }
            else
            {
                await store.ReleaseAsync(claim);
            }
        }

        return new Written(response.Body);
    }

    // The body is read whole and put back for the endpoint, within the cap of the request limits.
    private static async Task<string> HashAsync(HttpContext httpContext, ApiRequest api)
    {
        var request = httpContext.Request;
        var body = new MemoryStream();
        await request.Body.CopyToAsync(body, httpContext.RequestAborted);
        body.Position = 0;
        request.Body = body;
        httpContext.Response.RegisterForDispose(body);

        var principal = api.Operation.Principal;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in new[] { request.Method, request.Path.Value, request.QueryString.Value, api.Locale, MediaType(request.ContentType), $"{principal.Kind}:{principal.Id:D}", $"{api.Operation.Site.Value:D}" })
        {
            // Each part with its length, so that no two requests run together into the same text.
            var text = part ?? "";
            hash.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{text.Length}:{text}\n")));
        }

        hash.AppendData(body.GetBuffer(), 0, (int)body.Length);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // The media type alone, so that a charset parameter or its case does not make a retry another request.
    private static string? MediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed.MediaType.Value?.ToLowerInvariant() : contentType;

    // Writes the result into memory, so that what the client gets and what a repeat gets are the same bytes. Memory needs no
    // client, so the client going away does not stop the capture at once, only InProgressTimeout later, so that a request
    // whose client is gone holds its key no longer than a lease.
    private static async Task<StoredResponse> CaptureAsync(HttpContext httpContext, IResult result, IdempotencyStore store, IdempotencyClaim claim, CancellationToken aborted)
    {
        var response = httpContext.Response;
        var original = response.Body;
        using var buffer = new MemoryStream();
        using var capture = store.CaptureBound(aborted);
        response.Body = buffer;
        httpContext.RequestAborted = capture.Token;
        try
        {
            await result.ExecuteAsync(httpContext);
            await response.BodyWriter.FlushAsync(capture.Token);
        }
        catch (OperationCanceledException) when (capture.IsCancellationRequested)
        {
            store.NotCaptured(claim);
            throw;
        }
        finally
        {
            httpContext.RequestAborted = aborted;
            response.Body = original;
        }

        Dictionary<string, string> headers = [];
        foreach (var name in KeptHeaders)
        {
            if (response.Headers[name] is [{ } value])
            {
                headers[name] = value;
            }
        }

        return new StoredResponse(response.StatusCode, headers, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    private sealed class Written(string body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => Write(httpContext.Response, body);
    }

    private sealed class Replay(StoredResponse stored) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = stored.Status;
            foreach (var (name, value) in stored.Headers)
            {
                response.Headers[name] = value;
            }

            response.Headers[ReplayedHeader] = "true";
            return Write(response, stored.Body);
        }
    }

    private static Task Write(HttpResponse response, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength = bytes.Length;
        return bytes.Length == 0 ? Task.CompletedTask : response.Body.WriteAsync(bytes, 0, bytes.Length);
    }
}
