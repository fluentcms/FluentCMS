using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

// The commands that write what an instance answers with --url: export, schema snapshot --output and generate openapi. Each
// writes only an answer that is the instance's, and otherwise leaves the file at --output as it was.
public sealed class AnswerCheckTests : IDisposable
{
    private const string Url = "https://cms.example.com/";
    private const string Key = "fcms_mk_secret";
    private const string LastRun = "the file of the last run";

    private static readonly Uri SignIn = new("https://login.example.com/sign-in?return=%2Fapi%2Fmanagement");

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-answer-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Commands => ["export", "snapshot", "openapi"];

    public static TheoryData<string, string> NotTheInstances => new()
    {
        { "export", "redirect" }, { "export", "html" }, { "export", "not a document" }, { "export", "truncated" },
        { "snapshot", "redirect" }, { "snapshot", "html" }, { "snapshot", "not a document" }, { "snapshot", "truncated" },
        { "openapi", "redirect" }, { "openapi", "html" }, { "openapi", "not a document" }, { "openapi", "truncated" },
    };

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _directory.Delete(recursive: true);
    }

    [Theory]
    [MemberData(nameof(NotTheInstances))]
    public async Task AnAnswerThatIsNotTheInstancesIsRefusedAndTheFileIsLeftAsItWas(string command, string answer)
    {
        var file = Path.Combine(_directory.FullName, "out");
        await File.WriteAllTextAsync(file, LastRun, Token);

        (await RunAsync(command, file, AnswerOf(command, answer))).ShouldBe(1);

        _error.ToString().ShouldStartWith($"{(command == "export" ? "FCMS3253" : "FCMS3361")}: ");
        _error.ToString().ShouldNotContain(Key);
        _output.ToString().ShouldNotContain("Wrote");
        (await File.ReadAllTextAsync(file, Token)).ShouldBe(LastRun);
        _directory.EnumerateFiles().Select(entry => entry.Name).ShouldBe(["out"]);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task ARedirectNamesOnlyTheSchemeHostAndPortItPointsTo(string command)
    {
        var file = Path.Combine(_directory.FullName, "out");

        (await RunAsync(command, file, AnswerOf(command, "redirect"))).ShouldBe(1);

        _error.ToString().ShouldContain("302 Found, a redirect to https://login.example.com:443, which the tool does not follow; give --url the address the instance answers at.");
        _error.ToString().ShouldNotContain("sign-in");
        _error.ToString().ShouldNotContain("return");
        File.Exists(file).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task TheInstancesAnswerIsWrittenOverTheFile(string command)
    {
        var file = Path.Combine(_directory.FullName, "out");
        await File.WriteAllTextAsync(file, LastRun, Token);
        var answer = AnswerOf(command, "valid");

        (await RunAsync(command, file, answer)).ShouldBe(0, _error.ToString());

        _output.ToString().ShouldContain($"Wrote {file}");
        (await File.ReadAllBytesAsync(file, Token)).ShouldBe(answer.Body);
        _directory.EnumerateFiles().Select(entry => entry.Name).ShouldBe(["out"]);
    }

    [Fact]
    public async Task ImportAndTheCommandsThatAdministerAnInstanceRefuseARedirectWithTheirCodes()
    {
        var package = Path.Combine(_directory.FullName, "package.zip");
        await File.WriteAllBytesAsync(package, Package(), Token);
        var redirect = AnswerOf("export", "redirect");

        (await CommandLine.RunAsync(["import", package, "--url", Url, "--token", Key], _output, _error, launcher: null, _ => new HttpClient(redirect, disposeHandler: false), Token)).ShouldBe(1);
        _error.ToString().ShouldStartWith("FCMS3253: the instance answered 302 Found, a redirect to https://login.example.com:443, which the tool does not follow");

        _error.GetStringBuilder().Clear();
        (await CommandLine.RunAsync(["apikey", "list", "--url", Url, "--token", Key], _output, _error, launcher: null, _ => new HttpClient(redirect, disposeHandler: false), Token)).ShouldBe(1);
        _error.ToString().ShouldStartWith("FCMS3361: the instance answered 302 Found, a redirect to https://login.example.com:443, which the tool does not follow");
    }

    // The client the tool makes itself, against a server on this machine that redirects to a sign-in page of its own.
    [Fact]
    public async Task TheToolsOwnClientFollowsNoRedirect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = 0;
        var serving = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var connection = await listener.AcceptTcpClientAsync(Token);
                    await using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync(Token);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(Token)))
                    {
                    }

                    Interlocked.Increment(ref requests);
                    var answer = requestLine!.Contains("/login", StringComparison.Ordinal)
                        ? "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 7\r\nConnection: close\r\n\r\nSign in"
                        : "HTTP/1.1 302 Found\r\nLocation: /login?return=%2F\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), Token);
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or OperationCanceledException or IOException)
            {
            }
        }, Token);
        var file = Path.Combine(_directory.FullName, "snapshot.json");

        var exitCode = await CommandLine.RunAsync(["schema", "snapshot", "--output", file, "--url", $"http://127.0.0.1:{port}/", "--token", Key], _output, _error, cancellationToken: Token);
        listener.Stop();
        await serving;

        exitCode.ShouldBe(1);
        _error.ToString().ShouldStartWith($"FCMS3361: http://127.0.0.1:{port}/ answered schema/snapshot with 302 Found, a redirect to http://127.0.0.1:{port}, which the tool does not follow");
        Volatile.Read(ref requests).ShouldBe(1);
        File.Exists(file).ShouldBeFalse();
    }

    private Task<int> RunAsync(string command, string file, Answer answer)
    {
        string[] args = command switch
        {
            "export" => ["export", "--output", file],
            "snapshot" => ["schema", "snapshot", "--output", file],
            _ => ["generate", "openapi", "--output", file],
        };
        return CommandLine.RunAsync([.. args, "--url", Url, "--token", Key], _output, _error, launcher: null, _ => new HttpClient(answer, disposeHandler: false), Token);
    }

    private static Answer AnswerOf(string command, string kind)
    {
        var (mediaType, valid, notADocument) = command switch
        {
            "export" => ("application/zip", Package(), Encoding.UTF8.GetBytes("<html>Sign in</html>")),
            "snapshot" => ("application/json", Encoding.UTF8.GetBytes("""{"collections":[{"name":"article","fields":[{"name":"title","type":"text"}]}]}"""), Encoding.UTF8.GetBytes("""{"collections":"article"}""")),
            _ => ("application/json", Encoding.UTF8.GetBytes("""{"openapi":"3.1.1","info":{"title":"FluentCMS","version":"v1"},"paths":{}}"""), Encoding.UTF8.GetBytes("""{"title":"Sign in"}""")),
        };

        return kind switch
        {
            "redirect" => new Answer(HttpStatusCode.Found, "text/html", Encoding.UTF8.GetBytes("<a href=\"/sign-in\">Sign in</a>"), SignIn),
            "html" => new Answer(HttpStatusCode.OK, "text/html", Encoding.UTF8.GetBytes("<html>Sign in</html>")),
            "not a document" => new Answer(HttpStatusCode.OK, mediaType, notADocument),
            "truncated" => new Answer(HttpStatusCode.OK, mediaType, valid[..(valid.Length / 2)]),
            _ => new Answer(HttpStatusCode.OK, mediaType, valid),
        };
    }

    private static byte[] Package()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var manifest = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            manifest.Write("""{"format":1,"site":"default"}""");
        }

        return buffer.ToArray();
    }

    // Answers every request the same way.
    private sealed class Answer(HttpStatusCode status, string mediaType, byte[] body, Uri? location = null) : HttpMessageHandler
    {
        public byte[] Body => body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            var response = new HttpResponseMessage(status) { Content = content, RequestMessage = request };
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }
}
