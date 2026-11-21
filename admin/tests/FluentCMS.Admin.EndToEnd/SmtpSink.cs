using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FluentCMS.Admin.EndToEnd;

// Just enough SMTP on a loopback port for the host to hand over a message, without TLS: the host sends nothing in Production
// without a sender, and an invitation that cannot be sent is revoked at once. The messages go nowhere. Each connection is
// answered on its own and within a time, so that one that breaks off or stalls leaves the next answered.
internal sealed class SmtpSink : IDisposable
{
    // Far above what the host takes to send one message, and below MailKit's own timeout of two minutes.
    private static readonly TimeSpan ConversationLimit = TimeSpan.FromSeconds(30);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public SmtpSink()
    {
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = ConverseAsync(client);
        }
    }

    private async Task ConverseAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                limit.CancelAfter(ConversationLimit);
                await ConverseAsync(client.GetStream(), limit.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
            {
                // A client that broke off or stalled ends its own conversation only.
            }
        }
    }

    private static async Task ConverseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost ESMTP".AsMemory(), cancellationToken);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            switch (line.Split(' ', 2)[0].ToUpperInvariant())
            {
                case "EHLO":
                    await writer.WriteLineAsync("250 localhost".AsMemory(), cancellationToken);
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>".AsMemory(), cancellationToken);
                    string? body;
                    do
                    {
                        body = await reader.ReadLineAsync(cancellationToken);
                    }
                    while (body is not null and not ".");

                    await writer.WriteLineAsync("250 2.0.0 Queued".AsMemory(), cancellationToken);
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 2.0.0 Bye".AsMemory(), cancellationToken);
                    return;
                default:
                    await writer.WriteLineAsync("250 2.0.0 OK".AsMemory(), cancellationToken);
                    break;
            }
        }
    }
}
