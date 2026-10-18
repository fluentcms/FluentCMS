using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

public sealed partial class PasswordResetTests
{
    private const string NewPassword = "N3w!Passw0rd";

    private static readonly NewUser Editor = new("editor@example.com", "Editor", "An0ther!Passw0rd");

    [Fact]
    public async Task A_link_resets_the_password_once_and_both_steps_are_audited()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var editorId = await host.AsUserAsync(ownerId, async directory => (await directory.CreateAsync(Editor)).Value);
        await host.SignInAsync(AdminHost.Owner);

        // The link is in the response to the post, not in a redirect, and the screen does not show it again.
        var issued = await host.SubmitAsync("/users", $"reset-link-{editorId:N}", new Dictionary<string, string>());
        var html = await issued.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var link = ResetLinkIn(html);
        Assert.StartsWith($"{host.Client.BaseAddress}reset-password?user={editorId:N}&token=", link);
        Assert.InRange(ExpiryIn(html), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        Assert.DoesNotContain("reset-password?", await host.Client.GetStringAsync("/users"));

        await using var editorBrowser = host.OpenAnotherBrowser();
        var path = new Uri(link).PathAndQuery;
        Assert.Equal(HttpStatusCode.OK, (await editorBrowser.Client.GetAsync(path)).StatusCode);

        var mismatched = await editorBrowser.SubmitAsync(path, "reset-password", PasswordFields(NewPassword, "Different!Passw0rd"));
        Assert.Contains("The passwords do not match.", await mismatched.Content.ReadAsStringAsync());

        // Identity checks the token before the password, so a rejected password leaves the link usable.
        var weak = await editorBrowser.SubmitAsync(path, "reset-password", PasswordFields("short", "short"));
        Assert.Contains("aria-invalid=\"true\"", await weak.Content.ReadAsStringAsync());

        var reset = await editorBrowser.SubmitAsync(path, "reset-password", PasswordFields(NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/sign-in", editorBrowser.LocationOf(reset));
        Assert.False(editorBrowser.IsSignedIn);

        Assert.Contains("data-testid=\"sign-in-error\"", await (await editorBrowser.SignInAsync(Editor)).Content.ReadAsStringAsync());
        Assert.Equal("/", editorBrowser.LocationOf(await editorBrowser.SignInAsync(Editor.Email, NewPassword)));

        await using var stranger = host.OpenAnotherBrowser();
        var again = await stranger.SubmitAsync(path, "reset-password", PasswordFields("Th1rd!Passw0rd", "Th1rd!Passw0rd"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("data-testid=\"reset-link-invalid\"", await again.Content.ReadAsStringAsync());
        Assert.Equal("/", stranger.LocationOf(await stranger.SignInAsync(Editor.Email, NewPassword)));

        var rows = await AuditRowsAsync(host);
        var issuedEvent = Assert.Single(rows, row => row.Operation == "user.password_reset_issued");
        Assert.Equal(InitiatorKinds.User, issuedEvent.InitiatorKind);
        Assert.Equal(ownerId, issuedEvent.InitiatorId);
        Assert.Equal(AdminHost.Owner.DisplayName, issuedEvent.InitiatorName);
        Assert.Equal(editorId, issuedEvent.SubjectId);

        var resetEvent = Assert.Single(rows, row => row.Operation == "user.password_reset");
        Assert.Equal(InitiatorKinds.User, resetEvent.InitiatorKind);
        Assert.Equal(editorId, resetEvent.InitiatorId);
        Assert.Equal(Editor.DisplayName, resetEvent.InitiatorName);
        Assert.Equal("user", resetEvent.SubjectType);
        Assert.Equal(editorId, resetEvent.SubjectId);
    }

    [Fact]
    public async Task An_expired_link_is_refused_and_leaves_the_password_unchanged()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["PasswordReset:TokenLifespan"] = "00:00:01",
        });
        var ownerId = await host.CreateFirstAdministratorAsync();
        var editorId = await host.AsUserAsync(ownerId, async directory => (await directory.CreateAsync(Editor)).Value);
        var issued = await host.AsUserAsync<IPasswordReset, Result<PasswordResetToken>>(ownerId, reset => reset.IssueAsync(editorId));
        Assert.InRange(issued.Value.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));

        // Identity dates its tokens by the system clock rather than TimeProvider, so the lifetime has to pass for real.
        await Task.Delay(TimeSpan.FromSeconds(2));
        var expired = await host.SubmitAsync(ResetPath(editorId, issued.Value.Token), "reset-password", PasswordFields(NewPassword, NewPassword));

        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        Assert.Contains("data-testid=\"reset-link-invalid\"", await expired.Content.ReadAsStringAsync());
        Assert.Equal("/", host.LocationOf(await host.SignInAsync(Editor)));
        Assert.DoesNotContain(await AuditRowsAsync(host), row => row.Operation == "user.password_reset");
    }

    [Fact]
    public async Task A_disabled_user_gets_no_link_and_cannot_use_one_issued_before()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var editorId = await host.AsUserAsync(ownerId, async directory => (await directory.CreateAsync(Editor)).Value);
        var issued = await host.AsUserAsync<IPasswordReset, Result<PasswordResetToken>>(ownerId, reset => reset.IssueAsync(editorId));
        await host.AsUserAsync(ownerId, directory => directory.DisableAsync(editorId));

        var refused = await host.AsUserAsync<IPasswordReset, Result<PasswordResetToken>>(ownerId, reset => reset.IssueAsync(editorId));
        Assert.Equal(UserErrorCodes.UserDisabled, Assert.Single(refused.Errors).Code);

        await host.SignInAsync(AdminHost.Owner);
        var screen = await host.Client.GetStringAsync("/users");
        Assert.Contains($"reset-link-{ownerId:N}", screen);
        Assert.DoesNotContain($"reset-link-{editorId:N}", screen);

        await using var stranger = host.OpenAnotherBrowser();
        var used = await stranger.SubmitAsync(ResetPath(editorId, issued.Value.Token), "reset-password", PasswordFields(NewPassword, NewPassword));
        Assert.Contains("data-testid=\"reset-link-invalid\"", await used.Content.ReadAsStringAsync());

        await host.AsUserAsync(ownerId, directory => directory.EnableAsync(editorId));
        Assert.Equal("/", stranger.LocationOf(await stranger.SignInAsync(Editor)));
        Assert.Single(await AuditRowsAsync(host), row => row.Operation == "user.password_reset_issued");
        Assert.DoesNotContain(await AuditRowsAsync(host), row => row.Operation == "user.password_reset");
    }

    [Fact]
    public async Task A_reset_lifts_the_lockout_left_by_wrong_passwords()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var editorId = await host.AsUserAsync(ownerId, async directory => (await directory.CreateAsync(Editor)).Value);

        // Identity's default lockout is five failed attempts.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await host.SignInAsync(Editor.Email, "Wr0ng!Passw0rd");
        }

        var issued = await host.AsUserAsync<IPasswordReset, Result<PasswordResetToken>>(ownerId, reset => reset.IssueAsync(editorId));
        var reset = await host.SubmitAsync(ResetPath(editorId, issued.Value.Token), "reset-password", PasswordFields(NewPassword, NewPassword));

        Assert.Equal("/sign-in", host.LocationOf(reset));
        Assert.Equal("/", host.LocationOf(await host.SignInAsync(Editor.Email, NewPassword)));
    }

    [Fact]
    public async Task The_reset_page_opens_without_signing_in_while_the_users_screen_stays_behind_it()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();

        foreach (var route in new[] { "/reset-password", "/reset-password?user=not-a-user&token=x", $"/reset-password?user={ownerId:N}" })
        {
            var response = await host.Client.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("data-testid=\"reset-link-invalid\"", html);
            Assert.DoesNotContain("Form.Password", html);
        }

        var page = await host.Client.GetAsync(ResetPath(ownerId, "forged"));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Form.Password", await page.Content.ReadAsStringAsync());
        Assert.Contains("default-src 'self'", Assert.Single(page.Headers.GetValues("Content-Security-Policy")));

        var forged = await host.SubmitAsync(ResetPath(ownerId, "forged"), "reset-password", PasswordFields(NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        Assert.Contains("data-testid=\"reset-link-invalid\"", await forged.Content.ReadAsStringAsync());
        Assert.False(host.IsSignedIn);

        // A token from the sign-in page gets the post past anti-forgery, so only sign-in stands in its way.
        var issue = await host.SubmitAsync("/users", $"reset-link-{ownerId:N}", new Dictionary<string, string>(), from: "/sign-in");
        Assert.Equal(HttpStatusCode.Redirect, issue.StatusCode);
        Assert.StartsWith("/sign-in", host.LocationOf(issue));

        Assert.Equal("/", host.LocationOf(await host.SignInAsync(AdminHost.Owner)));
        Assert.DoesNotContain(await AuditRowsAsync(host), row => row.Operation.StartsWith("user.password_reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reset_posts_count_toward_the_sign_in_limit()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["SignInRateLimit:PermitLimit"] = "2",
            ["SignInRateLimit:Window"] = "00:01:00",
        });
        var ownerId = await host.CreateFirstAdministratorAsync();
        var path = ResetPath(ownerId, "forged");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SubmitAsync(path, "reset-password", PasswordFields(NewPassword, NewPassword))).StatusCode);
        }

        var throttled = await host.SubmitAsync(path, "reset-password", PasswordFields(NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    private static string ResetPath(Guid userId, string token)
    {
        return $"/reset-password?user={userId:N}&token={Uri.EscapeDataString(token)}";
    }

    private static Dictionary<string, string> PasswordFields(string password, string confirmation)
    {
        return new Dictionary<string, string>
        {
            ["Form.Password"] = password,
            ["Form.ConfirmPassword"] = confirmation,
        };
    }

    private static string ResetLinkIn(string html)
    {
        var match = ResetLink().Match(html);
        Assert.True(match.Success, "The page shows no reset link.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static DateTimeOffset ExpiryIn(string html)
    {
        var match = Expiry().Match(html);
        Assert.True(match.Success, "The page shows no expiry.");
        return DateTimeOffset.Parse(WebUtility.HtmlDecode(match.Groups[1].Value), CultureInfo.InvariantCulture);
    }

    private static async Task<List<Infrastructure.Audit.AuditEventRow>> AuditRowsAsync(AdminHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.ToListAsync();
        return rows.OrderBy(row => row.Timestamp).ToList();
    }

    [GeneratedRegex("id=\"reset-link\"[^>]*? value=\"([^\"]+)\"")]
    private static partial Regex ResetLink();

    [GeneratedRegex("<time datetime=\"([^\"]+)\"")]
    private static partial Regex Expiry();
}
