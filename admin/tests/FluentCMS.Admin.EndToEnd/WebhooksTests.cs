using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The webhooks half (spec 5.3) on the reference host, by keyboard: an administrator makes a webhook for the publish event to a
// receiver of the test, copies the secret made for it, which the page then holds nowhere, publishes an entry, reads the
// delivery in the log with its state, body and error, sends it again and pings, each reaching the receiver. The host runs in
// Production, where a webhook takes https only, and trusts no certificate of the run, so the receiver takes each connection
// and closes it before TLS completes: every attempt fails, which is what the log reads and the redelivery is for. In a site
// where the webhooks plugin is disabled the half has no menu item, its page is not found and it is never fetched.
public sealed class WebhooksTests(EndToEndHost host) : BrowserTest(host)
{
    private static readonly Regex WebhooksHalf = new(@"/_framework/FluentCMS\.Plugins\.Webhooks\.Admin(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> WebhooksTexts =
        XDocument.Load(Path.Combine(Repository.Root, "admin", "src", "plugins", "FluentCMS.Plugins.Webhooks.Admin", "WebhooksResources.resx")).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!, StringComparer.Ordinal);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAdministratorMakesAWebhookForAPublishCopiesItsSecretReadsTheDeliverySendsItAgainAndPings()
    {
        using var receiver = new Receiver();
        using var management = await Host.ManagementClientAsync();
        var name = $"Webhooks {Mark}";
        Guid? id = null;
        try
        {
            var browser = await OpenBrowserAsync();
            await browser.GrantClipboardAsync();
            var tab = await SignedInTabAsync(browser, "webhooks", await Host.CreateEditorAsync("Super Admin"));
            var main = tab.Page.Locator("main");
            await Expect(Heading(tab, Text("Webhooks.Page.Heading"))).ToBeVisibleAsync();

            var form = Region(tab, Text("Webhooks.Form.NewHeading"));
            await Field(form, Text("Webhooks.Form.Name")).FillAsync(name);
            await Field(form, Text("Webhooks.Form.Url")).FillAsync(receiver.Url);
            await Ui.PressAsync(Box(form, Ui.Format(Text("Webhooks.Form.Event"), Text("Webhooks.Event.content.published"), "content.published")), "Space");
            IResponse? created = null;
            var dialog = await tab.OpenDialogAsync(
                async () => created = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(form, Text("Webhooks.Form.Create")), "Enter"), "POST", tab.ManagementUrl("webhooks")),
                Text("Webhooks.Secret.CreatedTitle"));
            id = await Ui.CreatedIdAsync(created!, "webhooks");

            // The secret is copied, never shown, and gone from the page with the dialog.
            await Ui.PressAsync(Ui.ButtonIn(dialog, Repository.ComponentText("SecretCopy")), "Enter");
            await Expect(dialog.GetByText(Repository.ComponentText("SecretCopied"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
            var secret = await tab.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
            secret.ShouldStartWith("whsec_");
            (await browser.SecretsAsync()).ShouldContain(secret);
            await Ui.PressAsync(Ui.ButtonIn(dialog, Repository.ComponentText("SecretDone")), "Enter");
            await Expect(tab.ShownDialog(Text("Webhooks.Secret.CreatedTitle"))).ToHaveCountAsync(0);
            (await tab.Page.EvaluateAsync<bool>("secret => document.documentElement.outerHTML.includes(secret)", secret)).ShouldBeFalse();
            var stored = await management.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/webhooks/{id:D}", UriKind.Relative), Token);
            stored!.ContainsKey("secret").ShouldBeFalse();

            // A publish is delivered and fails at the receiver, which the log reads once the attempt has its result; the page
            // reads the log again on Refresh until then, since the attempt may still be queued or being sent.
            await PublishAsync(management, $"Webhooks {Mark}");
            await Ui.PressAsync(main.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = name, Exact = true }), "Enter");
            await Expect(Heading(tab, name)).ToBeVisibleAsync();
            var rows = main.Locator("tr.webhooks-attempt");
            var first = rows.First;
            await Ui.UntilAsync(
                async () =>
                {
                    if (await rows.CountAsync() > 0 && (await first.Locator("td").Nth(2).TextContentAsync())?.Trim() == Text("Webhooks.State.failed"))
                    {
                        return true;
                    }

                    await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(main, Text("Webhooks.Log.Refresh")), "Enter"), "GET", tab.ManagementUrl($"webhooks/{id:D}/deliveries?page=1&pageSize=20"));
                    return false;
                },
                "the publish's first attempt has failed in the log");
            await Expect(first.Locator("td").First).ToHaveTextAsync("content.published");
            receiver.Connections.ShouldBeGreaterThanOrEqualTo(1);

            var of = Ui.Format(Text("Webhooks.Log.OfAttempt"), "content.published", "1");
            await Ui.PressAsync(Ui.ButtonIn(first, Text("Webhooks.Log.Details") + of), "Enter");
            var details = main.Locator("tr.webhooks-attempt-details").First;
            await Expect(details.Locator(".webhooks-body")).ToContainTextAsync("\"event\": \"content.published\"");
            await Expect(details).ToContainTextAsync(Text("Webhooks.Log.Error"));
            await Expect(details).ToContainTextAsync(Text("Webhooks.Log.NoAnswer"));

            var message = main.Locator("p.webhooks-message");
            var before = receiver.Connections;
            var again = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(first, Text("Webhooks.Log.Redeliver") + of), "Enter"), response => response.Request.Method == "POST" && response.Url.EndsWith("/redeliver", StringComparison.Ordinal));
            again.Status.ShouldBe(202);
            await Expect(message).ToHaveTextAsync(new Regex("^" + Regex.Escape(Ui.Format(Text("Webhooks.Log.ResultFailed"), Text("Webhooks.Log.TheRedelivery"), ""))));
            receiver.Connections.ShouldBeGreaterThan(before);

            before = receiver.Connections;
            var ping = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(main, Text("Webhooks.Log.Ping")), "Enter"), "POST", tab.ManagementUrl($"webhooks/{id:D}/ping"));
            ping.Status.ShouldBe(202);
            await Expect(message).ToHaveTextAsync(new Regex("^" + Regex.Escape(Ui.Format(Text("Webhooks.Log.ResultFailed"), Text("Webhooks.Log.ThePing"), ""))));
            await Expect(rows.First).ToContainTextAsync("ping");
            receiver.Connections.ShouldBeGreaterThan(before);
            browser.PolicyViolations.ShouldBeEmpty();
        }
        finally
        {
            if (id is { } made)
            {
                using var removed = await management.DeleteAsync(new Uri($"api/management/v1/webhooks/{made:D}", UriKind.Relative), CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task InASiteWhereTheWebhooksPluginIsDisabledTheHalfHasNoMenuItemItsPageIsNotFoundAndItIsNeverFetched()
    {
        var site = Host.Sites[1];
        using var management = await Host.ManagementClientAsync();
        try
        {
            using (var disabled = await management.PutAsJsonAsync(new Uri($"api/management/v1/plugins/webhooks/sites/{site.Id}", UriKind.Relative), new { enabled = false }, Token))
            {
                disabled.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            var browser = await OpenBrowserAsync();
            var tab = await browser.NewTabAsync();
            var half = Requests(tab);
            await tab.Page.GotoAsync(new Uri(Host.Admin, "system/components").AbsoluteUri);
            await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync("Super Admin"));
            var entry = tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Text("Webhooks.Menu"), Exact = true });
            await Expect(entry).ToBeVisibleAsync();

            await tab.SwitchSiteAsync(site);
            await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Administration.Menu.Sites"), Exact = true })).ToBeVisibleAsync();
            await Expect(entry).ToHaveCountAsync(0);

            await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, "webhooks").AbsoluteUri));
            await Expect(tab.Heading("Shell.NotFound.Heading")).ToBeVisibleAsync();
            await Expect(tab.SiteSwitcher).ToHaveValueAsync(site.Id);
            half.ShouldBeEmpty();
            browser.PolicyViolations.ShouldBeEmpty();
        }
        finally
        {
            using var restored = await management.PutAsJsonAsync(new Uri($"api/management/v1/plugins/webhooks/sites/{site.Id}", UriKind.Relative), new { enabled = true }, CancellationToken.None);
        }
    }

    // A draft of the default site, published.
    private static async Task PublishAsync(HttpClient management, string title)
    {
        using var draft = await management.PostAsJsonAsync(new Uri("api/management/v1/content/article", UriKind.Relative), new JsonObject { ["title"] = title }, Token);
        var answer = await draft.Content.ReadAsStringAsync(Token);
        draft.StatusCode.ShouldBe(HttpStatusCode.Created, answer);
        var entry = JsonNode.Parse(answer)!["id"]!.GetValue<string>();
        using var published = await management.PostAsync(new Uri($"api/management/v1/content/article/{entry}/publish", UriKind.Relative), null, Token);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
    }

    private static string Text(string key) =>
        WebhooksTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"WebhooksResources.resx has no key {key}.");

    private static ILocator Heading(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = name, Exact = true, Level = 1 });

    private static ILocator Region(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Region, new LocatorGetByRoleOptions { Name = name, Exact = true });

    // A field by its label, which ends with a marker when a value is required.
    private static ILocator Field(ILocator scope, string label) => scope.GetByLabel(new Regex($@"^{Regex.Escape(label)}( \*)?$"));

    // The checkbox of the wrapped set whose label is the text.
    private static ILocator Box(ILocator scope, string label) =>
        scope.Locator("fluent-field").Filter(Ui.Exactly(label)).Locator("fluent-checkbox");

    private static ConcurrentQueue<string> Requests(AdminTab tab)
    {
        var fetched = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (WebhooksHalf.IsMatch(new Uri(request.Url).AbsolutePath))
            {
                fetched.Enqueue(request.Url);
            }
        };
        return fetched;
    }

    // A receiver on a loopback port of the test, which the host reaches since the run allows private networks to webhooks. It
    // counts each connection and closes it at once, so the host's TLS handshake fails and the attempt is recorded as failed.
    private sealed class Receiver : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _connections;

        public Receiver()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public string Url => $"https://{IPAddress.Loopback}:{((IPEndPoint)_listener.LocalEndpoint).Port}/hooks";

        public int Connections => Volatile.Read(ref _connections);

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (true)
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                }
                catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }
}
