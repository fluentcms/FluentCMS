using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;
using static FluentCMS.Cli.Tests.InstanceRun;

namespace FluentCMS.Cli.Tests;

// The commands with --url: they call the Management API with the key, as its principal, and say what the API refused.
public sealed class InstanceCommandTests(InstanceFixture instance) : IClassFixture<InstanceFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApiKeyCreatePrintsAKeyThatAuthenticatesAndRevokeEndsIt()
    {
        var run = new InstanceRun(instance.Handler);
        var admin = await instance.KeyAsync();

        (await run.RunAsync("apikey", "create", "--name", "ci", "--class", "management", "--role", BuiltInRoles.EditorName, "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(0, run.Error.ToString());
        var key = run.Output.ToString().Trim();
        key.ShouldStartWith("fcms_mk_");
        run.Error.ToString().ShouldContain("shown only once");
        (run.Output.ToString() + run.Error).ShouldNotContain(admin);

        using var client = instance.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        (await client.GetAsync("/api/management/v1/me", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await run.RunAsync("apikey", "list", "--json", "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(0, run.Error.ToString());
        var id = Items(run.Output.ToString()).Single(item => item.GetProperty("name").GetString() == "ci").GetProperty("id").GetString()!;

        (await run.RunAsync("apikey", "revoke", id, "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(0, run.Error.ToString());
        (await client.GetAsync("/api/management/v1/me", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await run.RunAsync("apikey", "list", "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(0, run.Error.ToString());
        run.Output.ToString().Split(Environment.NewLine).Single(line => line.StartsWith(id, StringComparison.Ordinal)).ShouldContain("revoked");
    }

    [Fact]
    public async Task TheKeyComesFromTheEnvironmentToo()
    {
        var run = new InstanceRun(instance.Handler);
        var admin = await instance.KeyAsync();
        var previous = Environment.GetEnvironmentVariable(InstanceOptions.TokenVariable);
        Environment.SetEnvironmentVariable(InstanceOptions.TokenVariable, admin);
        try
        {
            (await run.RunAsync("jobs", "list", "--json", "--url", InstanceFixture.Url)).ShouldBe(0, run.Error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(InstanceOptions.TokenVariable, previous);
        }

        using var json = JsonDocument.Parse(run.Output.ToString());
        json.RootElement.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        run.Output.ToString().Trim().ShouldNotContain('\n');
    }

    [Theory]
    [InlineData("apikey", "list")]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Public")]
    [InlineData("user", "invite", "--email", "refused@example.com", "--role", "Editor")]
    [InlineData("jobs", "list")]
    public async Task AKeyWithoutThePermissionOfACommandIsRefusedWithTheProblemOfTheApi(params string[] args)
    {
        var run = new InstanceRun(instance.Handler);
        var author = await instance.KeyAsync(BuiltInRoles.AuthorName);

        var exitCode = await run.RunAsync([.. args, "--url", InstanceFixture.Url, "--token", author]);

        exitCode.ShouldBe(1);
        run.Error.ToString().ShouldStartWith("FCMS3");
        run.Error.ToString().ShouldNotContain(author);
        run.Output.ToString().ShouldBeEmpty();
    }

    // A key that reads roles, which members.manage allows, reaches the endpoint of the command, which refuses it there.
    [Theory]
    [InlineData("apikey", "create", "--name", "x", "--class", "delivery", "--role", "Public")]
    [InlineData("user", "invite", "--email", "refused@example.com", "--role", "Editor")]
    public async Task TheEndpointOfTheCommandRefusesAKeyThatCanReadRoles(params string[] args)
    {
        var run = new InstanceRun(instance.Handler);
        var role = $"Members {Guid.NewGuid():N}";
        await instance.Host.CreateRoleAsync(role, ["members.manage"], Token);
        var key = await instance.KeyAsync(role);

        (await run.RunAsync([.. args, "--url", InstanceFixture.Url, "--token", key])).ShouldBe(1);

        run.Error.ToString().ShouldStartWith("FCMS3");
        run.Error.ToString().ShouldContain(args[0] == "apikey" ? "apikeys.manage" : "users.manage");
    }

    [Fact]
    public async Task UserInviteAndAddRoleCallTheirEndpoints()
    {
        var run = new InstanceRun(instance.Handler);
        var admin = await instance.KeyAsync();

        (await run.RunAsync("user", "invite", "--email", "remote@example.com", "--role", BuiltInRoles.EditorName, "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(0, run.Error.ToString());
        run.Output.ToString().ShouldStartWith("Invited remote@example.com; the invitation ");

        var user = await instance.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        (await run.RunAsync("user", "add-role", "--email", user.Email, "--role", BuiltInRoles.AuthorName, "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(0, run.Error.ToString());
        run.Output.ToString().ShouldContain("the role Author");

        (await run.RunAsync("user", "add-role", "--email", "nobody@example.com", "--role", BuiltInRoles.AuthorName, "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(1);
        run.Error.ToString().ShouldStartWith("FCMS3353");

        (await run.RunAsync("user", "add-role", "--email", user.Email, "--role", "Nobody", "--url", InstanceFixture.Url, "--token", admin)).ShouldBe(2);
        run.Error.ToString().ShouldStartWith("FCMS3350");
    }

    [Fact]
    public async Task AKeySentOverPlainHttpToAnotherMachineIsWarnedAbout()
    {
        var run = new InstanceRun(new JsonAnswer("""{"items":[],"total":0,"page":1,"pageSize":100}"""));

        (await run.RunAsync("jobs", "list", "--url", "http://cms.internal.example/", "--token", "fcms_mk_secret")).ShouldBe(0, run.Error.ToString());

        run.Error.ToString().ShouldStartWith("FCMS3363: http://cms.internal.example/ is http, so the key is sent without TLS");
        run.Error.ToString().ShouldNotContain("fcms_mk_secret");
        run.Output.ToString().ShouldStartWith("ID");
    }

    [Fact]
    public async Task AnInstanceThatCannotBeReachedIsAFailureWithItsAddressAndNotTheKey()
    {
        var run = new InstanceRun(new HttpClientHandler());

        (await run.RunAsync("apikey", "list", "--url", "http://127.0.0.1:9/", "--token", "fcms_mk_secret")).ShouldBe(1);

        run.Error.ToString().ShouldStartWith("FCMS3329: http://127.0.0.1:9/ could not be reached");
        run.Error.ToString().ShouldNotContain("fcms_mk_secret");
    }

    // The search matches a part of the address, oldest first, and more users match it than a page holds.
    [Fact]
    public async Task UserAddRoleReadsThePagesOfTheSearchUntilItFindsTheAddress()
    {
        var users = new PagedUsers("late@example.com", before: 150);
        var run = new InstanceRun(users);

        (await run.RunAsync("user", "add-role", "--email", "late@example.com", "--role", "Editor", "--url", "https://cms.example.com", "--token", "fcms_mk_secret")).ShouldBe(0, run.Error.ToString());

        run.Output.ToString().ShouldBe($"Gave late the role Editor.{Environment.NewLine}");
        users.Pages.ShouldBe([1, 2]);
        users.Granted.ShouldBe($"/api/management/v1/users/{PagedUsers.UserId}/roles/{PagedUsers.RoleId}");

        (await run.RunAsync("user", "add-role", "--email", "never@example.com", "--role", "Editor", "--url", "https://cms.example.com", "--token", "fcms_mk_secret")).ShouldBe(1);
        run.Error.ToString().ShouldStartWith("FCMS3353");
    }

    [Theory]
    [InlineData("schema", "snapshot")]
    [InlineData("schema", "diff", "--schema", "deployed.json")]
    [InlineData("doctor")]
    [InlineData("export", "--output", "out.zip")]
    [InlineData("import", "in.zip")]
    [InlineData("generate", "typescript", "--output", "out")]
    [InlineData("generate", "openapi", "--output", "openapi.json")]
    [InlineData("apikey", "list")]
    public async Task EveryCommandWithUrlWarnsOnceOfAKeySentOverPlainHttp(params string[] args)
    {
        var run = new InstanceRun(new Refusing());

        await run.RunAsync([.. args, "--url", "http://cms.internal.example/", "--token", "fcms_mk_secret"]);

        run.Error.ToString().ShouldStartWith("FCMS3363: http://cms.internal.example/ is http, so the key is sent without TLS");
        run.Error.ToString().Split('\n').Count(line => line.StartsWith("FCMS3363", StringComparison.Ordinal)).ShouldBe(1);
        run.Error.ToString().ShouldNotContain("fcms_mk_secret");
    }

    // A header cannot carry a line break, so the key and the site are refused before any request, with no stack trace.
    [Theory]
    [InlineData("schema", "snapshot")]
    [InlineData("doctor")]
    [InlineData("export", "--output", "out.zip")]
    [InlineData("import", "in.zip")]
    [InlineData("generate", "typescript", "--output", "out")]
    [InlineData("generate", "openapi", "--output", "openapi.json")]
    [InlineData("jobs", "list")]
    [InlineData("mcp")]
    public async Task AKeyWithALineBreakIsBadUsageBeforeAnyRequest(params string[] args)
    {
        var refusing = new Refusing();
        var run = new InstanceRun(refusing);

        (await run.RunAsync([.. args, "--url", "https://cms.example.com", "--token", "fcms_mk_one\r\nX-Injected: two"])).ShouldBe(2);

        run.Error.ToString().ShouldBe($"FCMS3316: the key holds a character that no API key has; the value is not repeated here.{Environment.NewLine}");
        refusing.Requests.ShouldBe(0);
    }

    [Theory]
    [InlineData("export", "--output", "out.zip")]
    [InlineData("import", "in.zip")]
    public async Task ASiteWithALineBreakIsBadUsageBeforeAnyRequest(params string[] args)
    {
        var refusing = new Refusing();
        var run = new InstanceRun(refusing);

        (await run.RunAsync([.. args, "--site", "blog\nX-Injected: 1", "--url", "https://cms.example.com", "--token", "fcms_mk_secret"])).ShouldBe(2);

        run.Error.ToString().ShouldContain("--site takes the slug or the id of a site");
        run.Error.ToString().ShouldNotContain("X-Injected");
        refusing.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task AProblemOfTheInstanceIsPrintedWithItsControlCharactersEscaped()
    {
        var esc = (char)0x1B;
        var problem = new JsonObject
        {
            ["code"] = "FCMS1003",
            ["detail"] = $"Refused{esc}[2J.",
            ["errors"] = new JsonArray(new JsonObject { ["code"] = "FCMS1004", ["path"] = "name", ["message"] = $"one\nFCMS0000: forged{esc}]0;title" }),
        };
        var run = new InstanceRun(new Refusing(problem.ToJsonString()));

        (await run.RunAsync("apikey", "list", "--url", "https://cms.example.com", "--token", "fcms_mk_secret")).ShouldBe(1);

        run.Error.ToString().ShouldBe($"FCMS1003: Refused\\u001B[2J.{Environment.NewLine}  FCMS1004: name: one\\u000AFCMS0000: forged\\u001B]0;title{Environment.NewLine}");
    }

    [Fact]
    public async Task TheLastErrorOfAJobIsPrintedWithItsControlCharactersEscaped()
    {
        var job = new JsonObject
        {
            ["id"] = "0190a3c4-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
            ["type"] = "webhook",
            ["queue"] = "default",
            ["status"] = "dead_lettered",
            ["attempts"] = 3,
            ["maxAttempts"] = 3,
            ["lastError"] = $"boom{(char)0x1B}[31m red",
        };
        var run = new InstanceRun(new JsonAnswer(new JsonObject { ["items"] = new JsonArray(job), ["total"] = 1 }.ToJsonString()));

        (await run.RunAsync("jobs", "list", "--url", "https://cms.example.com", "--token", "fcms_mk_secret")).ShouldBe(0, run.Error.ToString());

        run.Output.ToString().ShouldContain("boom\\u001B[31m red");
        run.Output.ToString().ShouldNotContain((char)0x1B);
    }

    [Fact]
    public async Task TheReportOfDoctorAndTheDiffOfAnInstanceArePrintedWithTheirControlCharactersEscaped()
    {
        var esc = (char)0x1B;
        var doctor = new InstanceRun(new JsonAnswer(new JsonObject { ["version"] = "1.0.0", ["environment"] = $"Production{esc}[2J" }.ToJsonString()));

        (await doctor.RunAsync("doctor", "--url", "https://cms.example.com", "--token", "fcms_mk_secret")).ShouldBe(0, doctor.Error.ToString());

        doctor.Output.ToString().ShouldStartWith("FluentCMS 1.0.0 in Production\\u001B[2J");
        doctor.Output.ToString().ShouldNotContain(esc);

        var deployed = Path.Combine(Path.GetTempPath(), $"fluentcms-empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(deployed, "{}");
        try
        {
            var snapshot = new JsonObject { ["collections"] = new JsonArray(new JsonObject { ["name"] = $"post{esc}]0;x", ["fields"] = new JsonArray() }) };
            var diff = new InstanceRun(new JsonAnswer(snapshot.ToJsonString()));

            (await diff.RunAsync("schema", "diff", "--schema", deployed, "--url", "https://cms.example.com", "--token", "fcms_mk_secret")).ShouldBe(1);

            diff.Output.ToString().ShouldContain("post\\u001B]0;x");
            diff.Output.ToString().ShouldNotContain(esc);
        }
        finally
        {
            File.Delete(deployed);
        }
    }

    // Ctrl+C cancels a command with --url: an export removes the file it was writing beside its target.
    [Fact]
    public async Task AnExportCancelledWhileItIsReadLeavesNothingBesideItsTarget()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-export-cancel-");
        try
        {
            var target = Path.Combine(directory.FullName, "site.zip");
            var stalled = new StalledPackage();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var running = CommandLine.RunAsync(["export", "--output", target, "--url", "https://cms.example.com", "--token", "fcms_mk_secret"], output, error, launcher: null, _ => new HttpClient(stalled), cancel.Token);
            await stalled.Copying.WaitAsync(TimeSpan.FromSeconds(30), Token);
            await cancel.CancelAsync();

            (await running).ShouldBe(1);
            Directory.EnumerateFileSystemEntries(directory.FullName).ShouldBeEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class JsonAnswer(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }

    // Answers every request with a problem, 403 unless a body is given, and counts them.
    private sealed class Refusing(string? problem = null) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(problem ?? """{"code":"FCMS1003","detail":"Refused."}""", System.Text.Encoding.UTF8, "application/problem+json"),
            });
        }
    }

    // The roles, and users in pages of 100: those before the one whose address is given match the search too.
    private sealed class PagedUsers(string email, int before) : HttpMessageHandler
    {
        public const string RoleId = "0190a3c4-0000-7000-8000-000000000001";
        public const string UserId = "0190a3c4-0000-7000-8000-000000000002";

        public List<int> Pages { get; } = [];

        public string? Granted { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                Granted = path;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (path.EndsWith("/roles", StringComparison.Ordinal))
            {
                return Json(new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = RoleId, ["name"] = "Editor" }) });
            }

            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var page = int.Parse(query["page"]!, System.Globalization.CultureInfo.InvariantCulture);
            var size = int.Parse(query["pageSize"]!, System.Globalization.CultureInfo.InvariantCulture);
            Pages.Add(page);
            var all = Enumerable.Range(0, before).Select(index => new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["email"] = $"x{index}{query["search"]}", ["userName"] = $"x{index}" })
                .Append(new JsonObject { ["id"] = UserId, ["email"] = email, ["userName"] = "late" })
                .Where(user => user["email"]!.GetValue<string>().Contains(query["search"]!, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Json(new JsonObject { ["items"] = new JsonArray([.. all.Skip((page - 1) * size).Take(size)]), ["total"] = all.Count, ["page"] = page, ["pageSize"] = size });
        }

        private static Task<HttpResponseMessage> Json(JsonObject body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json") });
    }

    // A package whose answer stalls after the signature of a zip, until the export is cancelled.
    private sealed class StalledPackage : HttpMessageHandler
    {
        private readonly TaskCompletionSource _copying = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Copying => _copying.Task;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new StalledStream(_copying));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class StalledStream(TaskCompletionSource copying) : Stream
    {
        private bool _started;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_started)
            {
                _started = true;
                byte[] signature = [0x50, 0x4B, 0x03, 0x04];
                signature.CopyTo(buffer);
                return signature.Length;
            }

            copying.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
