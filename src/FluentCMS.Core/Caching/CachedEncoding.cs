using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace FluentCMS.Core.Caching;

// The output cache copies a response's headers and body as they reach it, and asks its policies once the endpoint is done.
// Compression that wraps it encodes after the copy is made, and is still in the features then; a Content-Encoding without
// it was set inside the cache, so the copy would hold a body encoded for this request's Accept-Encoding alone, which the
// key does not vary on.
internal static class CachedEncoding
{
    public static bool EncodedInside(HttpContext context) =>
        context.Response.Headers.ContentEncoding.Count > 0 && context.Features.Get<IHttpsCompressionFeature>() is null;
}
