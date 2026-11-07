using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace FluentCMS.Core.Api.Common;

// A HEAD to a FluentCMS endpoint is answered as its GET without the body. The endpoint writes the body as for a GET and it
// is dropped here, outside the output cache: the cache stores a HEAD's response without its Content-Length and replays it
// with the length of the body it kept, so it has to keep the body for a hit to carry the right length.
internal sealed class HeadRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsHead(context.Request.Method) || context.GetEndpoint()?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is null
            || context.Features.Get<IHttpResponseBodyFeature>() is not { } body)
        {
            await next(context);
            return;
        }

        context.Features[typeof(IHttpResponseBodyFeature)] = new NoBody(body);
        try
        {
            await next(context);
        }
        finally
        {
            context.Features[typeof(IHttpResponseBodyFeature)] = body;
        }
    }

    private sealed class NoBody(IHttpResponseBodyFeature inner) : IHttpResponseBodyFeature
    {
        private PipeWriter? _writer;

        public Stream Stream => Stream.Null;

        public PipeWriter Writer => _writer ??= PipeWriter.Create(Stream.Null);

        public void DisableBuffering() => inner.DisableBuffering();

        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public Task CompleteAsync() => inner.CompleteAsync();
    }
}
