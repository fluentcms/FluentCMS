using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace FluentCMS.Services;

public interface IHttpLogChannel
{
    void Write(HttpLog httpLog);
    IAsyncEnumerable<HttpLog> ReadAllAsync(CancellationToken cancellationToken = default);
}

public class HttpLogChannel : IHttpLogChannel
{
    private readonly Channel<HttpLog> _logChannel;
    private readonly HttpLogConfig _config;

    public HttpLogChannel(IOptions<HttpLogConfig> options)
    {
        _logChannel = Channel.CreateUnbounded<HttpLog>();
        _config = options.Value ?? new HttpLogConfig();
    }

    public void Write(HttpLog httpLog)
    {
        if (!_config.Enable)
            return;

        if (!_logChannel.Writer.TryWrite(httpLog))
        {
            // Handle overflow if needed
            // TODO: For now, just ignore the log entry
        }
    }

    public IAsyncEnumerable<HttpLog> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _logChannel.Reader.ReadAllAsync(cancellationToken);
    }
}

