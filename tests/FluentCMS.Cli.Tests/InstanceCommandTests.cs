using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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

    private sealed class JsonAnswer(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}
