using System.Net;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

public sealed class UserScreenTests
{
    private static readonly NewUser Editor = new("editor@example.com", "Editor", "An0ther!Passw0rd");

    [Theory]
    [InlineData("/users", "/sign-in?ReturnUrl=%2Fusers")]
    [InlineData("/users/new", "/sign-in?ReturnUrl=%2Fusers%2Fnew")]
    public async Task An_anonymous_request_is_sent_to_sign_in(string route, string signIn)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var response = await host.Client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(signIn, host.LocationOf(response));
    }

    [Fact]
    public async Task An_administrator_sees_the_team_and_adds_a_user_who_can_then_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var before = await host.Client.GetStringAsync("/users");
        Assert.Contains("href=\"users\"", before);
        Assert.Contains("Owner", before);
        Assert.Contains("(you)", before);
        Assert.DoesNotContain($"disable-{ownerId:N}", before);

        var added = await host.SubmitAsync("/users/new", "add-user", NewUserFields(Editor));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Equal("/users", host.LocationOf(added));

        var editorId = (await host.UsersAsync()).Single(user => user.Email == Editor.Email).Id;
        var after = await host.Client.GetStringAsync("/users");
        Assert.Contains("editor@example.com", after);
        Assert.Contains($"disable-{editorId:N}", after);

        await using var editorBrowser = host.OpenAnotherBrowser();
        Assert.Equal("/", editorBrowser.LocationOf(await editorBrowser.SignInAsync(Editor)));

        var created = Assert.Single(await AuditRowsAsync(host), row => row.Operation == "user.created");
        Assert.Equal(ownerId, created.InitiatorId);
        Assert.Equal(editorId, created.SubjectId);
    }

    [Fact]
    public async Task Adding_a_user_reports_problems_next_to_the_fields_and_adds_nobody()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var duplicate = await host.SubmitAsync("/users/new", "add-user", NewUserFields(Editor with { Email = AdminHost.Owner.Email }));
        var html = await duplicate.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("is already taken", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Single(await host.UsersAsync());
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_open_session_within_a_minute_and_enabling_lets_them_back()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, configure: builder => builder.Services.AddSingleton<TimeProvider>(clock));
        var ownerId = await host.CreateFirstAdministratorAsync();
        var editorId = await host.AsUserAsync(ownerId, async directory => (await directory.CreateAsync(Editor)).Value);
        await host.SignInAsync(AdminHost.Owner);
        await using var editorBrowser = host.OpenAnotherBrowser();
        await editorBrowser.SignInAsync(Editor);
        Assert.Equal(HttpStatusCode.OK, (await editorBrowser.Client.GetAsync("/")).StatusCode);

        var disabled = await host.SubmitAsync("/users", $"disable-{editorId:N}", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Contains("Editor is disabled.", await disabled.Content.ReadAsStringAsync());
        Assert.True((await host.UsersAsync()).Single(user => user.Id == editorId).IsDisabled);

        // The cookie is checked against the security stamp once a minute, so the open session ends at the next check, and only that one.
        Assert.Equal(HttpStatusCode.OK, (await editorBrowser.Client.GetAsync("/")).StatusCode);
        clock.Now += TimeSpan.FromMinutes(2);
        var signedOut = await editorBrowser.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.StartsWith("/sign-in", editorBrowser.LocationOf(signedOut));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/")).StatusCode);
        Assert.Contains("data-testid=\"sign-in-error\"", await (await editorBrowser.SignInAsync(Editor)).Content.ReadAsStringAsync());

        var enabled = await host.SubmitAsync("/users", $"enable-{editorId:N}", new Dictionary<string, string>());
        Assert.Contains("Editor can sign in again.", await enabled.Content.ReadAsStringAsync());
        Assert.Equal("/", editorBrowser.LocationOf(await editorBrowser.SignInAsync(Editor)));

        var changes = (await AuditRowsAsync(host)).Where(row => row.Operation is "user.disabled" or "user.enabled").ToList();
        Assert.Equal(["user.disabled", "user.enabled"], changes.Select(row => row.Operation));
        Assert.All(changes, row =>
        {
            Assert.Equal(InitiatorKinds.User, row.InitiatorKind);
            Assert.Equal(ownerId, row.InitiatorId);
            Assert.Equal(AdminHost.Owner.DisplayName, row.InitiatorName);
            Assert.Equal(editorId, row.SubjectId);
        });
    }

    private static Dictionary<string, string> NewUserFields(NewUser user)
    {
        return new Dictionary<string, string>
        {
            ["Form.DisplayName"] = user.DisplayName,
            ["Form.Email"] = user.Email,
            ["Form.Password"] = user.Password,
            ["Form.ConfirmPassword"] = user.Password,
        };
    }

    private static async Task<List<Infrastructure.Audit.AuditEventRow>> AuditRowsAsync(AdminHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.ToListAsync();
        return rows.OrderBy(row => row.Timestamp).ToList();
    }
}
