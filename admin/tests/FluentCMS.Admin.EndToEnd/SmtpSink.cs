using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FluentCMS.Admin.EndToEnd;

// Just enough SMTP on a loopback port for the host to hand over a message, without TLS: the host sends nothing in Production
// without a sender, and an invitation that cannot be sent is revoked at once. The messages go nowhere.
internal sealed class SmtpSink : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public SmtpSink()
    {
        _listener.Start();
        _ = ServeAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await ConverseAsync(client.GetStream());
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
        {
        }
    }

    private async Task ConverseAsync(NetworkStream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost ESMTP");
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
            switch (line.Split(' ', 2)[0].ToUpperInvariant())
            {
                case "EHLO":
                    await writer.WriteLineAsync("250 localhost");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    string? body;
                    do
                    {
                        body = await reader.ReadLineAsync(_stop.Token);
                    }
                    while (body is not null and not ".");

                    await writer.WriteLineAsync("250 2.0.0 Queued");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 2.0.0 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 2.0.0 OK");
                    break;
            }
        }
    }
}
