using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace FluentCMS.Core.Api.Common;

// A request over the cap is a BadHttpRequestException with status 413, which the exception handler writes as a problem.
// Kestrel throws the same exception from the feature, so a body that grows past the cap while it is read ends the same way.
internal sealed class RequestLimitsMiddleware(RequestDelegate next, RequestLimitsOptions options)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null)
        {
            var cap = options.MaxBodySize;
            if (context.Request.ContentLength > cap)
            {
                throw TooLarge(cap);
            }

            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = cap;
            }
            else
            {
                context.Request.Body = new LimitedStream(context.Request.Body, cap);
            }
        }

        return next(context);
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
