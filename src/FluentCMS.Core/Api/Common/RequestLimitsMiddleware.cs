using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace FluentCMS.Core.Api.Common;

// A request over the cap is a BadHttpRequestException with status 413, which the exception handler writes as a problem.
// Kestrel throws the same exception from the feature, so a body that grows past the cap while it is read ends the same way.
// The cap never raises a limit the host or Kestrel already set, and an endpoint that sets its own limit keeps it. It runs
// before authentication, because the authorization server reads the body of a token, introspection or revocation request
// there, before the endpoint runs, and a path under a group of core that no endpoint answers is capped by its prefix, as
// the coarse limit counts it.
internal sealed class RequestLimitsMiddleware(RequestDelegate next, RequestLimitsOptions options)
{
    private static readonly PathString[] Prefixes = [.. FluentCmsEndpointGroups.Prefixes.Select(entry => new PathString(entry.Prefix))];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Applies(context))
        {
            await next(context);
            return;
        }

        var cap = options.MaxBodySize;
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            cap = feature.MaxRequestBodySize is { } current ? Math.Min(current, cap) : cap;
            feature.MaxRequestBodySize = cap;
        }
        else
        {
            context.Request.Body = new LimitedStream(context.Request.Body, cap);
        }

        if (context.Request.ContentLength > cap)
        {
            throw TooLarge(cap);
        }

        await next(context);

        // A body bound to a typed parameter ends in a bare 413 that the endpoint wrote itself.
        if (context.Response.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted && context.Response.ContentType is null)
        {
            await ProblemWriter.WriteAsync(context, ProblemMapper.Known(LimitErrorCodes.RequestTooLarge, TooLarge(cap).Message), cancellationToken: context.RequestAborted);
        }
    }

    private static bool Applies(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is not null)
        {
            return false;
        }

        return endpoint?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null
            || Prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix));
    }

    private static BadHttpRequestException TooLarge(long cap) =>
        new($"The request body is larger than the {cap} bytes allowed.", StatusCodes.Status413PayloadTooLarge);

    private sealed class LimitedStream(Stream inner, long cap) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _read += read;
            return _read > cap ? throw TooLarge(cap) : read;
        }
    }
}
