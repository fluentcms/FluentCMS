using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.Security;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Webhooks;

public sealed partial class WebhookScreenTests
{
    private static readonly Dictionary<string, string?> AllowExampleCom = new() { ["Outbound:AllowedHosts:0"] = "example.com" };

    [Fact]
    public async Task An_anonymous_request_is_sent_to_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var response = await host.Client.GetAsync("/webhooks");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Fwebhooks", host.LocationOf(response));
    }

    [Fact]
    public async Task An_administrator_adds_a_webhook_whose_secret_is_shown_once_and_stored_encrypted()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: AllowExampleCom);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var form = await host.Client.GetStringAsync("/webhooks");
        Assert.Contains("href=\"webhooks\"", form);
        Assert.Matches("name=\"Form.Published\"[^>]*checked", form);
        Assert.Contains("No webhook is set up yet.", form);

        var added = await host.SubmitAsync("/webhooks", "add-webhook", Frontend());
        var html = await added.Content.ReadAsStringAsync();

        // The secret is in the response to the post, not in a redirect, and no page shows it again.
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var secret = SecretIn(html);
        Assert.StartsWith(WebhookSecrets.Prefix, secret, StringComparison.Ordinal);

        var list = await host.Client.GetStringAsync("/webhooks");
        Assert.Contains("https://hooks.example.com/fluentcms", list);
        Assert.Contains("data-status=\"enabled\"", list);
        Assert.DoesNotContain(secret, list);
        Assert.DoesNotContain("data-testid=\"webhook-secret\"", list);

        await using var scope = host.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        var row = Assert.Single(await context.WebhookSubscriptions.AsNoTracking().ToListAsync());
        Assert.Equal([WebhookEvents.ChangeSetPublished], row.Events);
        Assert.DoesNotContain(secret[WebhookSecrets.Prefix.Length..], row.ProtectedSecret, StringComparison.Ordinal);
        Assert.Equal(secret, scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect(WebhookSecrets.ProtectionPurpose, row.ProtectedSecret));

        var addition = Assert.Single(await context.AuditEvents.AsNoTracking().Where(row => row.Operation == "webhook.added").ToListAsync());
        Assert.Equal((ownerId, row.Id), (addition.InitiatorId, addition.SubjectId!.Value));
    }

    [Fact]
    public async Task A_url_off_the_allow_list_is_reported_next_to_its_field_and_nothing_is_added()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: AllowExampleCom);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var fields = Frontend();
        fields["Form.Url"] = "https://hooks.other.test/fluentcms";
        fields.Remove("Form.Published");

        var refused = await host.SubmitAsync("/webhooks", "add-webhook", fields);
        var html = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("is not on the outbound allow-list", html);
        Assert.Contains("id=\"events-error\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.DoesNotContain("data-testid=\"webhook-secret\"", html);

        await using var scope = host.App.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().WebhookSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Disabling_from_the_list_marks_the_webhook_disabled()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: AllowExampleCom);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);
        await host.SubmitAsync("/webhooks", "add-webhook", Frontend());
        Guid id;
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().WebhookSubscriptions.SingleAsync()).Id;
        }

        var disabled = await host.SubmitAsync("/webhooks", $"disable-{id:N}", new Dictionary<string, string>());
        var html = await disabled.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Contains("The webhook to https://hooks.example.com/fluentcms is disabled.", html);
        Assert.Contains("data-status=\"disabled\"", html);
        Assert.DoesNotContain($"disable-{id:N}", html);

        await using var check = host.App.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        Assert.NotNull((await context.WebhookSubscriptions.AsNoTracking().SingleAsync()).DisabledAt);
        var disabling = Assert.Single(await context.AuditEvents.AsNoTracking().Where(row => row.Operation == "webhook.disabled").ToListAsync());
        Assert.Equal((ownerId, id), (disabling.InitiatorId, disabling.SubjectId!.Value));
    }

    [Fact]
    public async Task A_test_sent_from_the_list_reaches_the_endpoint_and_shows_under_recent_deliveries()
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["Outbound:AllowedHosts:0"] = receiver.Host,
            ["JobQueue:PollingInterval"] = "00:00:00.050",
        });
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);
        var fields = Frontend();
        fields["Form.Url"] = receiver.Url.ToString();
        await host.SubmitAsync("/webhooks", "add-webhook", fields);
        Guid id;
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().WebhookSubscriptions.SingleAsync()).Id;
        }

        var queued = await (await host.SubmitAsync("/webhooks", $"test-{id:N}", new Dictionary<string, string>())).Content.ReadAsStringAsync();
        await receiver.WaitForAsync(1);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string list;
        while (!(list = await host.Client.GetStringAsync("/webhooks")).Contains("data-delivery=\"delivered\"", StringComparison.Ordinal))
        {
            Assert.True(DateTime.UtcNow < deadline, "The delivery did not show in time.");
            await Task.Delay(50);
        }

        Assert.Contains("A test delivery to", queued);
        Assert.Contains("delivered (200)", list);
    }

    private static Dictionary<string, string> Frontend()
    {
        return new Dictionary<string, string>
        {
            ["Form.Url"] = "https://hooks.example.com/fluentcms",
            ["Form.Published"] = "true",
        };
    }

    private static string SecretIn(string html)
    {
        var match = SecretField().Match(html);
        Assert.True(match.Success, "The response shows no secret.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("data-testid=\"webhook-secret\".*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex SecretField();
}
