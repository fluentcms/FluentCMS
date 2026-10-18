using System.Net;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

public sealed class SignInTests
{
    private static readonly NewUser Editor = new("editor@example.com", "Editor", "An0ther!Passw0rd");

    [Fact]
    public async Task A_fresh_install_leads_every_admin_route_to_setup_until_the_first_administrator_exists()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, ["--environment", "Development"]);

        foreach (var route in new[] { "/", "/ui-spike", "/sign-in", "/sign-out" })
        {
            var (path, response) = await host.FollowAsync(route);
            Assert.Equal("/setup", path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var created = await host.SubmitAsync("/setup", "setup", new Dictionary<string, string>
        {
            ["Form.DisplayName"] = AdminHost.Owner.DisplayName,
            ["Form.Email"] = AdminHost.Owner.Email,
            ["Form.Password"] = AdminHost.Owner.Password,
            ["Form.ConfirmPassword"] = AdminHost.Owner.Password,
        });

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/", host.LocationOf(created));
        Assert.True(host.IsSignedIn);

        var home = await host.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains(">Owner</span>", await home.Content.ReadAsStringAsync());

        var setupAgain = await host.Client.GetAsync("/setup");
        Assert.Equal("/sign-in", host.LocationOf(setupAgain));
    }

    [Fact]
    public async Task Setup_reports_problems_next_to_the_fields_and_creates_nobody()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);

        var mismatch = await host.SubmitAsync("/setup", "setup", new Dictionary<string, string>
        {
            ["Form.DisplayName"] = "Owner",
            ["Form.Email"] = "owner@example.com",
            ["Form.Password"] = "Str0ng!Passw0rd",
            ["Form.ConfirmPassword"] = "Different!Passw0rd",
        });
        var weak = await host.SubmitAsync("/setup", "setup", new Dictionary<string, string>
        {
            ["Form.DisplayName"] = "Owner",
            ["Form.Email"] = "owner@example.com",
            ["Form.Password"] = "short",
            ["Form.ConfirmPassword"] = "short",
        });

        var mismatchHtml = await mismatch.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        Assert.Contains("The passwords do not match.", mismatchHtml);
        Assert.Contains("aria-invalid=\"true\"", mismatchHtml);
        Assert.Contains("value=\"owner@example.com\"", mismatchHtml);

        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
        Assert.Contains("Passwords must be at least", await weak.Content.ReadAsStringAsync());
        Assert.False(host.IsSignedIn);

        await using var scope = host.App.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IFirstRunSetup>().NeedsFirstAdministratorAsync());
    }

    [Fact]
    public async Task An_anonymous_request_is_sent_to_sign_in_and_returns_to_the_route_it_asked_for()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, ["--environment", "Development"]);
        await host.CreateFirstAdministratorAsync();

        var anonymous = await host.Client.GetAsync("/ui-spike");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Fui-spike", host.LocationOf(anonymous));

        var signedIn = await host.SignInAsync(AdminHost.Owner, host.LocationOf(anonymous)!);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/ui-spike", host.LocationOf(signedIn));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/ui-spike")).StatusCode);
    }

    [Theory]
    [InlineData("https%3A%2F%2Fevil.example%2F")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("%2F%5Cevil.example")]
    public async Task A_return_url_to_another_site_lands_on_the_admin_home(string returnUrl)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var signedIn = await host.SignInAsync(AdminHost.Owner, "/sign-in?ReturnUrl=" + returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/", host.LocationOf(signedIn));
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_email_and_a_disabled_account_fail_alike_and_issue_no_cookie()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.AsUserAsync(ownerId, async directory => await directory.DisableAsync((await directory.CreateAsync(Editor)).Value));

        var attempts = new[]
        {
            await host.SignInAsync(AdminHost.Owner.Email, "Wr0ng!Passw0rd"),
            await host.SignInAsync("nobody@example.com", AdminHost.Owner.Password),
            await host.SignInAsync(Editor),
        };

        foreach (var attempt in attempts)
        {
            Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
            Assert.Contains("data-testid=\"sign-in-error\"", await attempt.Content.ReadAsStringAsync());
        }

        Assert.False(host.IsSignedIn);
        Assert.Equal(HttpStatusCode.Redirect, (await host.Client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        // Identity's default lockout is five failed attempts.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await host.SignInAsync(AdminHost.Owner.Email, "Wr0ng!Passw0rd");
        }

        var locked = await host.SignInAsync(AdminHost.Owner);

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("data-testid=\"sign-in-error\"", await locked.Content.ReadAsStringAsync());
        Assert.False(host.IsSignedIn);
    }

    [Fact]
    public async Task Signing_in_and_out_is_audited_with_the_user_as_initiator()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();

        var signedIn = await host.SignInAsync(AdminHost.Owner);
        Assert.Equal("/", host.LocationOf(signedIn));

        // The sign-out button in the main layout posts to the sign-out page's handler.
        var home = await host.Client.GetStringAsync("/");
        Assert.Contains("action=\"sign-out\"", home);
        var signedOut = await host.SubmitAsync("/sign-out", "sign-out", new Dictionary<string, string>(), from: "/");

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/sign-in", host.LocationOf(signedOut));
        Assert.False(host.IsSignedIn);
        Assert.Equal(HttpStatusCode.Redirect, (await host.Client.GetAsync("/")).StatusCode);

        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = (await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.ToListAsync())
            .OrderBy(row => row.Timestamp)
            .ToList();

        Assert.Equal(["user.signed_in", "user.signed_out"], rows.Select(row => row.Operation));
        Assert.All(rows, row =>
        {
            Assert.Equal(InitiatorKinds.User, row.InitiatorKind);
            Assert.Equal(ownerId, row.InitiatorId);
            Assert.Equal(AdminHost.Owner.DisplayName, row.InitiatorName);
            Assert.Null(row.AgentCredentialId);
            Assert.Equal("user", row.SubjectType);
            Assert.Equal(ownerId, row.SubjectId);
        });
    }
}
