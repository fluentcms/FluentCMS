using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;

namespace FluentCMS.Host.Tests;

// Just enough SMTP on a loopback port for MailKit to hand over a message: no TLS, AUTH PLAIN accepted as sent.
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _messages = [];
    private readonly List<string> _logins = [];
    private readonly Task _serving;

    public FakeSmtpServer()
    {
        _listener.Start();
        _serving = ServeAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    // The decoded AUTH PLAIN credentials, as "user:password".
    public IReadOnlyList<string> Logins
    {
        get
        {
            lock (_logins)
            {
                return [.. _logins];
            }
        }
    }

    public static MimeMessage Parse(string data)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        return MimeMessage.Load(stream);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _serving;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await ConverseAsync(reader, writer);
        }
    }

    private async Task ConverseAsync(StreamReader reader, StreamWriter writer)
    {
        await writer.WriteLineAsync("220 localhost ESMTP");
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
            var command = line.Split(' ', 2)[0].ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 AUTH PLAIN");
                    break;
                case "AUTH":
                    var parts = line.Split(' ');
                    var response = parts.Length > 2 ? parts[2] : await AskAsync(reader, writer);
                    var fields = Encoding.UTF8.GetString(Convert.FromBase64String(response)).Split('\0');
                    lock (_logins)
                    {
                        _logins.Add($"{fields[1]}:{fields[2]}");
                    }

                    await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync(_stop.Token) is { } body && body != ".")
                    {
                        data.AppendLine(body.StartsWith("..", StringComparison.Ordinal) ? body[1..] : body);
                    }

                    lock (_messages)
                    {
                        _messages.Add(data.ToString());
                    }

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

    private async Task<string> AskAsync(StreamReader reader, StreamWriter writer)
    {
        await writer.WriteLineAsync("334 ");
        return await reader.ReadLineAsync(_stop.Token) ?? "";
    }
}
