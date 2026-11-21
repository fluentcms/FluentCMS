using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookSecretTests
{
    private const string Root = "/api/management/v1/webhooks";
    private const string Token1 = "Bearer token-of-the-receiver-that-must-not-leak";
    private const string Token2 = "Bearer second-token-that-must-not-leak";
    private const string Old = "the-old-secret-of-the-receiver";
    private const string New = "the-new-secret-of-the-receiver";

    [Fact]
    public async Task HeaderValuesAreProtectedAtRestInNoAnswerAndStillSent()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var logs = new LogCapture();
        var directory = Directory.CreateTempSubdirectory("fluentcms-webhooks-");
        try
        {
            List<string> answers = [];
            await using (var host = Host(logs: logs).UseSqlite(Path.Combine(directory.FullName, "cms.db")))
            {
                using var client = await AdminAsync(host);
                var url = receiver.Url().ToString();
                var created = await CreateAsync(client, new { name = "builds", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = Token1, ["X-Build"] = "site" } });
                var id = Id(created);
                var webhook = $"{Root}/{id:D}";

                var raw = (await RawAsync(host, "headers"))!;
                raw.ShouldNotStartWith("{");
                raw.ShouldNotContain("Authorization");
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    scope.ServiceProvider.GetRequiredService<WebhookSigning>().UnprotectHeaders(raw).ShouldBe(new Dictionary<string, string> { ["Authorization"] = Token1, ["X-Build"] = "site" });
                }

                using var read = await client.GetAsync(new Uri(webhook, UriKind.Relative), Token);
                using var list = await client.GetAsync(new Uri(Root, UriKind.Relative), Token);

                // Sent back as read, with one header renamed in case, one changed and one added, the redacted value keeps
                // the stored one.
                var replaced = new { name = "builds", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["authorization"] = WebhookService.RedactedValue, ["X-Build"] = "pages", ["X-Second"] = Token2 } };
                using var changed = await client.PutAsJsonAsync(new Uri(webhook, UriKind.Relative), replaced, Token);
                using var unknown = await client.PutAsJsonAsync(new Uri(webhook, UriKind.Relative), new { name = "builds", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["X-Missing"] = WebhookService.RedactedValue } }, Token);
                using var redactedCreate = await client.PostAsJsonAsync(new Uri(Root, UriKind.Relative), new { name = "other", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = WebhookService.RedactedValue } }, Token);

                answers.AddRange([created.ToJsonString(), await TextAsync(read), await TextAsync(list), await TextAsync(changed)]);
                created["headers"]!.AsObject().Select(pair => (pair.Key, pair.Value!.GetValue<string>())).ShouldBe([("Authorization", "***"), ("X-Build", "***")]);
                JsonNode.Parse(await TextAsync(read))!["headers"]!.AsObject().Select(pair => (pair.Key, pair.Value!.GetValue<string>())).ShouldBe([("Authorization", "***"), ("X-Build", "***")]);
                JsonNode.Parse(await TextAsync(list))!["items"]![0]!["headers"]!["Authorization"]!.GetValue<string>().ShouldBe("***");
                changed.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(changed));
                JsonNode.Parse(await TextAsync(changed))!["headers"]!.AsObject().Select(pair => (pair.Key, pair.Value!.GetValue<string>())).ShouldBe([("authorization", "***"), ("X-Build", "***"), ("X-Second", "***")]);
                unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
                (await TextAsync(unknown)).ShouldContain("headers.X-Missing");
                redactedCreate.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "a create has no stored value to keep");

                var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
                await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Sent" }, Token)).Id, cancellationToken: Token);
                await PumpAsync(host);

                var request = receiver.Requests.ShouldHaveSingleItem();
                request.Headers["Authorization"].ShouldBe(Token1);
                request.Headers["X-Build"].ShouldBe("pages");
                request.Headers["X-Second"].ShouldBe(Token2);
                using var log = await client.GetAsync(new Uri($"{webhook}/deliveries", UriKind.Relative), Token);
                answers.Add(await TextAsync(log));
            }

            foreach (var answer in answers)
            {
                answer.ShouldNotContain("token-of-the-receiver");
                answer.ShouldNotContain("second-token");
            }

            logs.Lines.ShouldAllBe(line => !line.Contains("token-of-the-receiver") && !line.Contains("second-token"));
            foreach (var file in directory.EnumerateFiles())
            {
                var bytes = await File.ReadAllBytesAsync(file.FullName, Token);
                bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("token-of-the-receiver")).ShouldBe(-1, file.Name);
                bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("second-token")).ShouldBe(-1, file.Name);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AWebhookStoredWithPlainHeadersIsStillSentAndItsHeadersAreProtectedOnTheNextWriteOrStart()
    {
        const string legacy = """{"Authorization":"Bearer legacy-token"}""";
        await using var receiver = await TestReceiver.StartAsync();
        var directory = Directory.CreateTempSubdirectory("fluentcms-webhooks-");
        try
        {
            var file = Path.Combine(directory.FullName, "cms.db");
            Guid started;
            await using (var before = Host().UseSqlite(file))
            {
                using var client = await AdminAsync(before);
                var url = receiver.Url().ToString();
                started = Id(await CreateAsync(client, new { name = "at start", url, events = new List<string> { "content.published" } }));
                var saved = Id(await CreateAsync(client, new { name = "on save", url, events = new List<string> { "content.published" } }));
                await using (var scope = before.Services.CreateAsyncScope())
                {
                    var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
                    (await database.Database.ExecuteSqlRawAsync("UPDATE cms_webhooks_subscription SET headers = {0}", [legacy], Token)).ShouldBe(2);
                    scope.ServiceProvider.GetRequiredService<WebhookSigning>().UnprotectHeaders(legacy).ShouldBe(new Dictionary<string, string> { ["Authorization"] = "Bearer legacy-token" });
                }

                using var read = await client.GetAsync(new Uri($"{Root}/{started:D}", UriKind.Relative), Token);
                JsonNode.Parse(await TextAsync(read))!["headers"]!["Authorization"]!.GetValue<string>().ShouldBe("***");
                var articles = before.As(await before.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
                await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Legacy" }, Token)).Id, cancellationToken: Token);
                await PumpAsync(before);
                receiver.Requests.Count.ShouldBe(2);

                using var resaved = await client.PutAsJsonAsync(new Uri($"{Root}/{saved:D}", UriKind.Relative), new { name = "on save", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = "***" } }, Token);
                resaved.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(resaved));
                (await RawAsync(before, "headers", saved))!.ShouldNotStartWith("{", customMessage: "a write protects them");
            }

            // A row the start of the next host finds plain, whatever the start of this one did.
            await using (var connection = new SqliteConnection($"Data Source={file};Pooling=False"))
            {
                await connection.OpenAsync(Token);
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE cms_webhooks_subscription SET headers = $headers WHERE name = 'at start'";
                command.Parameters.AddWithValue("$headers", legacy);
                (await command.ExecuteNonQueryAsync(Token)).ShouldBe(1);
            }

            await using var after = Host().UseSqlite(file);
            await StartedAsync(after);
            (await RawAsync(after, "headers", started))!.ShouldNotStartWith("{", customMessage: "the start protects them");
            var again = after.As(await after.CreateUserAsync(BuiltInRoles.SuperAdminName, "second", Token)).Content<Article>();
            await again.PublishAsync((await again.CreateAsync(new Article { Title = "Protected" }, Token)).Id, cancellationToken: Token);
            await PumpAsync(after);
            receiver.Requests.Count.ShouldBe(4);
            receiver.Requests.ShouldAllBe(request => request.Headers["Authorization"] == "Bearer legacy-token");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ARotatedSecretSignsBesideTheNewOneUntilTheOverlapEnds()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host();
        await MaintainedAsync(host);
        using var client = await AdminAsync(host);
        var url = receiver.Url().ToString();
        var id = Id(await CreateAsync(client, new { name = "rotated", url, events = new List<string> { "content.published" }, secret = Old }));
        var webhook = new Uri($"{Root}/{id:D}", UriKind.Relative);
        var rotatedAt = host.Time.GetUtcNow();

        using var rotated = await client.PutAsJsonAsync(webhook, new { name = "rotated", url, events = new List<string> { "content.published" }, secret = New }, Token);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(rotated));
        JsonNode.Parse(await TextAsync(rotated))!["previousSecretExpiresAt"]!.GetValue<DateTimeOffset>().ShouldBe(rotatedAt + TimeSpan.FromHours(24));
        var previous = (await RawAsync(host, "previous_secret"))!;
        previous.ShouldNotContain(Old);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<WebhookSigning>().Unprotect(previous).ShouldBe(Old);
        }

        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "During" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
        var during = receiver.Requests.ShouldHaveSingleItem();
        var signature = during.Headers[WebhookSender.SignatureHeader];
        Signatures(signature).ShouldBe(2);
        signature.ShouldBe(WebhookSigning.Signature(host.Time.GetUtcNow().ToUnixTimeSeconds(), during.Body, New, Old), customMessage: "the new secret signs first");
        Verifies(signature, Old, during.Body, host.Time.GetUtcNow()).ShouldBeTrue("a receiver that has the old secret");
        Verifies(signature, New, during.Body, host.Time.GetUtcNow()).ShouldBeTrue("a receiver that has the new secret");

        host.Time.Advance(TimeSpan.FromHours(24));
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "After" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
        var after = receiver.Requests[1];
        Signatures(after.Headers[WebhookSender.SignatureHeader]).ShouldBe(1);
        Verifies(after.Headers[WebhookSender.SignatureHeader], New, after.Body, host.Time.GetUtcNow()).ShouldBeTrue();
        Verifies(after.Headers[WebhookSender.SignatureHeader], Old, after.Body, host.Time.GetUtcNow()).ShouldBeFalse();

        using var read = await client.GetAsync(webhook, Token);
        JsonNode.Parse(await TextAsync(read))!.AsObject().ContainsKey("previousSecretExpiresAt").ShouldBeFalse();
        (await RawAsync(host, "previous_secret")).ShouldNotBeNull("an ended overlap needs nothing to clear it");
        using var saved = await client.PutAsJsonAsync(webhook, new { name = "renamed", url, events = new List<string> { "content.published" } }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(saved));
        (await RawAsync(host, "previous_secret")).ShouldBeNull("the next write drops it");
    }

    [Fact]
    public async Task WithNoOverlapTheRotatedSecretStopsSigningAtOnce()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host().WithServices(services => services.PostConfigure<WebhooksOptions>(options => options.SecretOverlap = TimeSpan.Zero));
        using var client = await AdminAsync(host);
        var url = receiver.Url().ToString();
        var id = Id(await CreateAsync(client, new { name = "rotated", url, events = new List<string> { "content.published" }, secret = Old }));

        using var rotated = await client.PutAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { name = "rotated", url, events = new List<string> { "content.published" }, secret = New }, Token);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(rotated));
        JsonNode.Parse(await TextAsync(rotated))!.AsObject().ContainsKey("previousSecretExpiresAt").ShouldBeFalse();
        (await RawAsync(host, "previous_secret")).ShouldBeNull();

        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Now" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
        var request = receiver.Requests.ShouldHaveSingleItem();
        Signatures(request.Headers[WebhookSender.SignatureHeader]).ShouldBe(1);
        Verifies(request.Headers[WebhookSender.SignatureHeader], New, request.Body, host.Time.GetUtcNow()).ShouldBeTrue();
    }

    // A redacted value used to keep the stored one whatever the URL, so a caller who may manage the webhook but does not know
    // its headers could point it at a host of their own and receive them.
    [Fact]
    public async Task ARedactedHeaderIsKeptOnlyWhileTheUrlKeepsItsSchemeHostAndPort()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var other = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        var url = receiver.Url();
        var webhook = $"{Root}/{Id(await CreateAsync(client, new { name = "builds", url = url.ToString(), events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = Token1 } })):D}";

        object Replaced(string to, string value) => new { name = "builds", url = to, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = value } };
        foreach (var moved in (string[])[$"http://localhost:{url.Port}/hook", other.Url().ToString()])
        {
            using var refused = await client.PutAsJsonAsync(new Uri(webhook, UriKind.Relative), Replaced(moved, WebhookService.RedactedValue), Token);
            var text = await TextAsync(refused);
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);
            var error = JsonNode.Parse(text)!["errors"]!.AsArray().ShouldHaveSingleItem()!;
            (error["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()).ShouldBe(("headers.Authorization", WebhookErrorCodes.InvalidField));
            error["message"]!.GetValue<string>().ShouldContain("host");
        }

        // Another path, query or case of the scheme keeps it.
        using (var kept = await client.PutAsJsonAsync(new Uri(webhook, UriKind.Relative), Replaced($"HTTP://127.0.0.1:{url.Port}/other?build=1", WebhookService.RedactedValue), Token))
        {
            kept.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(kept));
        }

        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Kept" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
        var request = receiver.Requests.ShouldHaveSingleItem();
        (request.Path, request.Headers["Authorization"]).ShouldBe(("/other", Token1));

        // Sent again, the value goes with the new host.
        using (var moved = await client.PutAsJsonAsync(new Uri(webhook, UriKind.Relative), Replaced(other.Url().ToString(), Token2), Token))
        {
            moved.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(moved));
        }

        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Moved" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
        other.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe(Token2);
        receiver.Requests.Count.ShouldBe(1);

        // A host name compares without case, and the scheme alone is a change too.
        var port = other.Url().Port;
        foreach (var (to, value, status) in ((string, string, HttpStatusCode)[])
        [
            ($"http://localhost:{port}/a", Token2, HttpStatusCode.OK),
            ($"http://LocalHost:{port}/b", WebhookService.RedactedValue, HttpStatusCode.OK),
            ($"https://localhost:{port}/b", WebhookService.RedactedValue, HttpStatusCode.UnprocessableEntity),
        ])
        {
            using var response = await client.PutAsJsonAsync(new Uri(webhook, UriKind.Relative), Replaced(to, value), Token);
            response.StatusCode.ShouldBe(status, $"{to}: {await TextAsync(response)}");
        }
    }

    // A PUT that kept the URL it read wrote only the headers, so a PUT that moved the webhook in between kept its URL beside
    // headers that were checked against the old one.
    [Fact]
    public async Task AKeptHeaderIsWrittenWithTheUrlItWasCheckedAgainst()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var moved = new MoveBeforeUpdate("https://elsewhere.example/hook");
        await using var host = Host().Configure(cms => cms.Services.ConfigureDbContext<PluginDbContext<WebhooksPlugin>>(options => options.AddInterceptors(moved)));
        using var client = await AdminAsync(host);
        var url = receiver.Url().ToString();
        var id = Id(await CreateAsync(client, new { name = "builds", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = Token1 } }));

        moved.Armed = true;
        using var kept = await client.PutAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { name = "builds", url, events = new List<string> { "content.published" }, headers = new Dictionary<string, string> { ["Authorization"] = WebhookService.RedactedValue } }, Token);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(kept));
        moved.Armed.ShouldBeFalse("the other write ran before this one's update");

        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<Webhook>().AsNoTracking().SingleAsync(webhook => webhook.Id == id, Token)).Url.ShouldBe(url);
    }

    [Fact]
    public void WhatTheLogStoresOfAnAnswerHasNoControlCharacterOrHalfAPair()
    {
        WebhookSender.Storable("a\0b\u0001c\td\r\ne\u007F", 100).ShouldBe("abc\td\r\ne");
        WebhookSender.Storable("ab\U0001F600", 3).ShouldBe("ab");
        WebhookSender.Storable("ab\U0001F600", 4).ShouldBe("ab\U0001F600");
        WebhookSender.Storable("\0abc", 2).ShouldBe("ab");
    }

    // The column as the database holds it, of the only webhook or of the one named.
    private static async Task<string?> RawAsync(FluentCmsTestHost host, string column, Guid? id = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        var rows = await database.Database
            .SqlQueryRaw<RawRow>("SELECT id AS \"Id\", headers AS \"Headers\", previous_secret AS \"PreviousSecret\" FROM cms_webhooks_subscription")
            .ToListAsync(Token);
        var row = id is { } only ? rows.Single(item => item.Id == only) : rows.ShouldHaveSingleItem();
        return column switch
        {
            "headers" => row.Headers,
            "previous_secret" => row.PreviousSecret,
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };
    }

    private sealed class RawRow
    {
        public Guid Id { get; set; }

        public string Headers { get; set; } = "";

        public string? PreviousSecret { get; set; }
    }

    // Moves the webhook to another URL just before the next update of it, as a PUT that commits in between would.
    private sealed class MoveBeforeUpdate(string url) : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await MoveAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await MoveAsync(command, cancellationToken);
            return result;
        }

        private async Task MoveAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!Armed || !command.CommandText.Contains("UPDATE \"cms_webhooks_subscription\"", StringComparison.Ordinal))
            {
                return;
            }

            Armed = false;
            await using var other = command.Connection!.CreateCommand();
            other.Transaction = command.Transaction;
            other.CommandText = $"UPDATE cms_webhooks_subscription SET url = '{url}'";
            await other.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
