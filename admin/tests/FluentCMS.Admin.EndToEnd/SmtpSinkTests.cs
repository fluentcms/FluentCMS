using System.Net;
using System.Net.Sockets;
using System.Text;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.EndToEnd;

// The mail server of the run, without the host: a client that stalls or breaks off in the middle of a message leaves the next
// one answered, so that an invitation the host sends later is not held until MailKit's timeout and revoked.
public sealed class SmtpSinkTests
{
    private static readonly TimeSpan AnswerLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AClientThatStallsOrBreaksOffLeavesTheNextOneAnswered()
    {
        using var sink = new SmtpSink();

        using var stalled = new TcpClient();
        await stalled.ConnectAsync(IPAddress.Loopback, sink.Port, TestContext.Current.CancellationToken);

        using (var broken = new TcpClient())
        {
            await broken.ConnectAsync(IPAddress.Loopback, sink.Port, TestContext.Current.CancellationToken);
            var (reader, writer) = Open(broken);
            (await AnswerAsync(reader)).ShouldStartWith("220");
            await writer.WriteLineAsync("EHLO test");
            (await AnswerAsync(reader)).ShouldStartWith("250");
            await writer.WriteLineAsync("DATA");
            (await AnswerAsync(reader)).ShouldStartWith("354");

            // A reset instead of the end of the message.
            broken.Client.LingerState = new LingerOption(true, 0);
        }

        using var next = new TcpClient();
        await next.ConnectAsync(IPAddress.Loopback, sink.Port, TestContext.Current.CancellationToken);
        var (nextReader, nextWriter) = Open(next);
        (await AnswerAsync(nextReader)).ShouldStartWith("220");
        foreach (var command in (string[])["EHLO test", "MAIL FROM:<cms@example.com>", "RCPT TO:<sam@example.com>"])
        {
            await nextWriter.WriteLineAsync(command);
            (await AnswerAsync(nextReader)).ShouldStartWith("250");
        }

        await nextWriter.WriteLineAsync("DATA");
        (await AnswerAsync(nextReader)).ShouldStartWith("354");
        await nextWriter.WriteLineAsync("Subject: An invitation\r\n\r\nThe link.\r\n.");
        (await AnswerAsync(nextReader)).ShouldStartWith("250");
        await nextWriter.WriteLineAsync("QUIT");
        (await AnswerAsync(nextReader)).ShouldStartWith("221");
    }

    private static (StreamReader Reader, StreamWriter Writer) Open(TcpClient client)
    {
        var stream = client.GetStream();
        return (new StreamReader(stream, Encoding.ASCII), new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true });
    }

    private static async Task<string> AnswerAsync(StreamReader reader)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(AnswerLimit);
        return await reader.ReadLineAsync(limit.Token) ?? "";
    }
}
