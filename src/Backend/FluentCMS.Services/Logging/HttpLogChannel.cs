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

    public HttpLogChannel()
    {
        _logChannel = Channel.CreateUnbounded<HttpLog>();
    }

    public void Write(HttpLog httpLog)
    {
        if (!_logChannel.Writer.TryWrite(httpLog))
        {
            // Handle overflow if needed
            // TODO: For now, just ignore the log entry
        }
    }

    public IAsyncEnumerable<HttpLog> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return _logChannel.Reader.ReadAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {

            throw;
        }
        
    }
}

