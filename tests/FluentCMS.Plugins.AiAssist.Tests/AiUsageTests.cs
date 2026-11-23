using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// cms_ai_usage: one row for every call that is sent to a provider, and GET /ai/usage over the rows of a site.
public sealed class AiUsageTests : IClassFixture<AiHost>
{
    // In the text, the suggestion and the provider's failure of every call here, and in no row or log line after them.
    private const string Marker = "ships-that-must-not-be-kept";

    private const int CalledEvent = 9360;

    private readonly AiHost _ai;

    public AiUsageTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task EveryCallWritesOneRowWithItsCallerItsProviderWhatItUsedAndHowItEnded()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var body = new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." };
        _ai.Chat.Answer = $"Ships are large vessels, {Marker}.";
        (_ai.Chat.PromptTokens, _ai.Chat.CompletionTokens) = (40, 9);

        // With the site's own credential a row holds what the provider reported, 0 for a call that failed before it did.
        _ai.Chat.SiteCredential = true;

        var answered = await SuggestAsync(admin, "rewrite", body);
        _ai.Chat.Failure = new InvalidOperationException($"The provider could not read: {Marker}.");
        using var failed = await ActAsync(admin, "shorten", body);
        var problem = await JsonAsync(failed, HttpStatusCode.BadGateway);

        problem["code"]!.GetValue<string>().ShouldBe("FCMS1689");
        problem.ToJsonString().ShouldNotContain(Marker);
        var rows = await _ai.UsageAsync(site);
        rows.Count.ShouldBe(2);
        var row = rows[0];
        row.PrincipalKind.ShouldBe("apiKey");
        row.PrincipalId.ShouldBe(_ai.PrincipalOf(admin));
        (row.Action, row.Type, row.Field).ShouldBe(("rewrite", "article", "summary"));
        (row.Provider, row.Model).ShouldBe((ScriptedFactory.Provider, "test-model"));
        (row.PromptTokens, row.CompletionTokens, row.Succeeded).ShouldBe((40, 9, true));
        row.DurationMs.ShouldBe(answered["usage"]!["durationMs"]!.GetValue<long>());
        row.CreatedAt.ShouldBe(_ai.Host.Time.GetUtcNow());
        (rows[1].Action, rows[1].Succeeded, rows[1].PromptTokens, rows[1].CompletionTokens).ShouldBe(("shorten", false, 0, 0));

        // Neither the rows nor the log hold the text, the suggestion or what the provider said.
        foreach (var kept in rows)
        {
            string.Join(' ', kept.PrincipalKind, kept.Action, kept.Type, kept.Field, kept.Provider, kept.Model).ShouldNotContain(Marker);
        }

        _ai.Logs.Of(CalledEvent).Count(line => line.Contains(site.ToString(), StringComparison.Ordinal)).ShouldBe(2);
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACallWhoseCallerGoesAwayIsCancelledAndWritesItsRow()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Hang = true;
        var recorded = _ai.Logs.NextAsync(CalledEvent);
        using var gone = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var call = ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." }, cancellationToken: gone.Token);
        await _ai.Chat.Entered.WaitAsync(Token);
        await gone.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => call);
        await recorded.WaitAsync(Token);

        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Action, row.Succeeded).ShouldBe(("rewrite", false));
        _ai.Logs.Of(CalledEvent).ShouldContain(line => line.Contains(site.ToString(), StringComparison.Ordinal) && line.Contains("cancelled", StringComparison.Ordinal));
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStreamWhoseCallerGoesAwayIsCancelledAndWritesItsRow()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Pieces = ["Ships are ", "large vessels."];
        _ai.Chat.Hang = true;
        var recorded = _ai.Logs.NextAsync(CalledEvent);

        using (var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true))
        {
            await using var events = await response.Content.ReadAsStreamAsync(Token);
            using var reader = new StreamReader(events);
            (await reader.ReadLineAsync(Token)).ShouldBe("event: delta");
        }

        await recorded.WaitAsync(Token);

        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        row.Succeeded.ShouldBeFalse();
        _ai.Logs.Of(CalledEvent).ShouldContain(line => line.Contains(site.ToString(), StringComparison.Ordinal) && line.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheUsageOfASiteNeedsItsPermissionAndIsFilteredByPrincipalActionAndTime()
    {
        var site = await _ai.SiteAsync();
        var elsewhere = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        using var editor = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.read", null), ("content.article.update", null));
        using var auditor = await _ai.CallerAsync(site, (AiPermissions.ReadUsage, null));
        using var other = await _ai.AdminAsync(elsewhere);
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };
        (_ai.Chat.PromptTokens, _ai.Chat.CompletionTokens) = (10, 3);
        _ai.Chat.SiteCredential = true;

        await SuggestAsync(admin, "rewrite", body);
        await SuggestAsync(editor, "rewrite", body);
        _ai.Host.Time.Advance(TimeSpan.FromHours(1));
        var later = _ai.Host.Time.GetUtcNow();
        await SuggestAsync(editor, "shorten", body);
        _ai.Chat.Failure = new InvalidOperationException("The provider broke.");
        (await ActAsync(editor, "shorten", body)).Dispose();
        _ai.Chat.Failure = null;
        await SuggestAsync(other, "rewrite", body);

        var all = await UsageAsync(auditor, "");
        var ofEditor = await UsageAsync(auditor, $"?principalId={_ai.PrincipalOf(editor)}");
        var shortened = await UsageAsync(auditor, "?action=shorten");
        var since = await UsageAsync(auditor, $"?from={Uri.EscapeDataString(later.ToString("O"))}");
        var sinceElsewhere = await UsageAsync(auditor, $"?from={Uri.EscapeDataString(later.ToOffset(TimeSpan.FromHours(5.5)).ToString("O"))}");
        var before = await UsageAsync(auditor, $"?to={Uri.EscapeDataString(later.ToString("O"))}");
        var paged = await UsageAsync(auditor, "?page=2&pageSize=3");

        // The rows of the site alone, the newest first, with the totals of every row the filter matches.
        all["total"]!.GetValue<long>().ShouldBe(4);
        (all["page"]!.GetValue<int>(), all["pageSize"]!.GetValue<int>()).ShouldBe((1, 50));
        var items = all["items"]!.AsArray();
        items.Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["shorten", "shorten", "rewrite", "rewrite"]);
        items.Select(item => item!["succeeded"]!.GetValue<bool>()).ShouldBe([false, true, true, true]);
        var newest = items[1]!;
        newest["principalKind"]!.GetValue<string>().ShouldBe("apiKey");
        newest["principalId"]!.GetValue<Guid>().ShouldBe(_ai.PrincipalOf(editor));
        (newest["type"]!.GetValue<string>(), newest["field"]!.GetValue<string>()).ShouldBe(("article", "summary"));
        (newest["provider"]!.GetValue<string>(), newest["model"]!.GetValue<string>()).ShouldBe((ScriptedFactory.Provider, "test-model"));
        newest["createdAt"]!.GetValue<DateTimeOffset>().ShouldBe(later);
        Totals(all).ShouldBe((4, 30, 9));
        var (adminId, editorId) = (_ai.PrincipalOf(admin), _ai.PrincipalOf(editor));
        Groups(all).ShouldBe(InOrder([("apiKey", editorId, "shorten", 2, 10, 3), ("apiKey", adminId, "rewrite", 1, 10, 3), ("apiKey", editorId, "rewrite", 1, 10, 3)]));

        ofEditor["total"]!.GetValue<long>().ShouldBe(3);
        Totals(ofEditor).ShouldBe((3, 20, 6));
        Groups(ofEditor).ShouldBe([("apiKey", editorId, "rewrite", 1, 10, 3), ("apiKey", editorId, "shorten", 2, 10, 3)]);
        shortened["total"]!.GetValue<long>().ShouldBe(2);
        Totals(shortened).ShouldBe((2, 10, 3));
        since["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["shorten", "shorten"]);
        sinceElsewhere["total"]!.GetValue<long>().ShouldBe(2);
        before["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["rewrite", "rewrite"]);
        paged["items"]!.AsArray().ShouldHaveSingleItem()!["action"]!.GetValue<string>().ShouldBe("rewrite");
        Totals(paged).ShouldBe((4, 30, 9));
        Groups(paged).Count.ShouldBe(3);

        // The caller who may run actions may not read the log, and a parameter that cannot be read is refused.
        using var refused = await editor.GetAsync(new Uri(Usage, UriKind.Relative), Token);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreach (var query in new[] { "?principalId=someone", "?action=paraphrase", "?from=2026-11-01T00:00:00", "?to=yesterday", "?page=0", "?pageSize=101", "?page=2147483647", "?page=42949674&pageSize=100" })
        {
            using var invalid = await auditor.GetAsync(new Uri(Usage + query, UriKind.Relative), Token);
            (await RefusedAsync(invalid, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680", query);
        }
    }

    // #3418: a row names its principal by the name it had at the call, as a version names its author, which a later rename
    // leaves as it was, and a caller who may only read the log reads it.
    [Fact]
    public async Task ARowNamesItsPrincipalAsItWasAtTheCall()
    {
        var site = await _ai.SiteAsync();
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };
        var user = await _ai.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var key = await _ai.Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, "Build key", Token);
        using var auditor = await _ai.CallerAsync(site, (AiPermissions.ReadUsage, null));
        var renamed = $"ada{Guid.NewGuid():N}";
        string name;

        using (var before = InSite(await _ai.Host.AsUserAsync(user), site))
        {
            await SuggestAsync(before, "rewrite", body);
        }

        using (var client = InSite(_ai.Host.AsKey(key), site))
        {
            await SuggestAsync(client, "shorten", body);
        }

        await using (var scope = _ai.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Seed, new SiteId(site));
            var ai = scope.ServiceProvider.GetRequiredService<AiAssistService>();
            await ai.RunAsync(await ai.PrepareAsync("title", new AiActionRequest("article", "summary", null, "Ships are big boats.", null, null), Token), null, Token);
        }

        await using (var scope = _ai.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            name = stored.UserName!;
            (await users.SetUserNameAsync(stored, renamed)).Succeeded.ShouldBeTrue();
        }

        using (var after = InSite(await _ai.Host.AsUserAsync(user), site))
        {
            await SuggestAsync(after, "expand", body);
        }

        var items = (await UsageAsync(auditor, ""))["items"]!.AsArray();
        items.Select(item => (item!["action"]!.GetValue<string>(), item["principalKind"]!.GetValue<string>(), item["principalId"]!.GetValue<Guid>(), item["principalName"]!.GetValue<string>()))
            .ShouldBe(
                [("expand", "adminUser", user.Id, renamed), ("title", "system", Guid.Empty, "System"), ("shorten", "apiKey", key.Id, "Build key"), ("rewrite", "adminUser", user.Id, name)],
                ignoreOrder: true);
    }

    // A name past the column is cut, and never between the two halves of a surrogate pair, which PostgreSQL would refuse.
    [Fact]
    public async Task ANameLongerThanTheColumnIsCutOutsideASurrogatePair()
    {
        var site = await _ai.SiteAsync();
        var user = await _ai.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var kept = new string('a', AiUsageModel.PrincipalNameMaxLength - 1);
        var named = user with { Principal = new Principal(PrincipalKind.AdminUser, user.Id, kept + "\U0001F6A2 and more", user.Principal.Claims) };

        await _ai.Host.As(named).In(new SiteId(site)).RunAsync<AiAssistService, AiActionResult>(async ai =>
            await ai.RunAsync(await ai.PrepareAsync("title", new AiActionRequest("article", "summary", null, "Ships are big boats.", null, null), Token), null, Token));

        (await _ai.UsageAsync(site)).ShouldHaveSingleItem().PrincipalName.ShouldBe(kept);
        AiAssistService.Limit(kept + "\U0001F6A2", AiUsageModel.PrincipalNameMaxLength).ShouldBe(kept);
        AiAssistService.Limit(kept + "b\U0001F6A2", AiUsageModel.PrincipalNameMaxLength).ShouldBe(kept + "b");
    }

    // #3449: the name is written through IActivityLog.ActorName, as core writes the author of a version: a value shaped like
    // a secret is masked, and a name past 128 characters is cut, here before a surrogate pair it would cut in two.
    [Fact]
    public async Task ANameIsStoredAsCoreStoresTheAuthorOfAVersion()
    {
        var site = await _ai.SiteAsync();
        var user = await _ai.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var kept = new string('a', AiUsageModel.PrincipalNameMaxLength - 1);
        List<string> authors = [];

        foreach (var name in (string[])["Build token=s3cr3t-value", kept + "\U0001F6A2 and more"])
        {
            var actor = _ai.Host.As(user with { Principal = new Principal(PrincipalKind.AdminUser, user.Id, name, user.Principal.Claims) }).In(new SiteId(site));
            await actor.RunAsync<AiAssistService, AiActionResult>(async ai =>
                await ai.RunAsync(await ai.PrepareAsync("title", new AiActionRequest("article", "summary", null, "Ships are big boats.", null, null), Token), null, Token));
            var article = await actor.Content<Article>().CreateAsync(new Article { Title = "Ships" }, Token);
            await using var scope = _ai.Services.CreateAsyncScope();
            authors.Add(await scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<ContentVersion>().Where(version => version.DocumentId == article.Id).Select(version => version.AuthorName).SingleAsync(Token));
        }

        authors.ShouldBe(["Build token=***", kept]);
        (await _ai.UsageAsync(site)).Select(row => row.PrincipalName).ShouldBe(authors, ignoreOrder: true);
    }

    // #3449: totals.groups holds the MaxGroups groups of principal and action that used the most tokens, ordered by kind, id
    // and action, and groupsTruncated says that the filter matched more; the totals still count every row. A filter that
    // matches fewer groups has them all.
    [Fact]
    public async Task TheGroupsAreThoseThatUsedTheMostTokensUpToTheirBound()
    {
        var site = await _ai.SiteAsync();
        using var auditor = await _ai.CallerAsync(site, (AiPermissions.ReadUsage, null));
        var principals = Enumerable.Range(0, 12).Select(_ => Guid.CreateVersion7()).ToList();
        var groups = principals.SelectMany(principal => AiActions.Names.Select(action => (Principal: principal, Action: action))).ToList();
        groups.Count.ShouldBeGreaterThan(AiUsageService.MaxGroups);
        await using (var scope = _ai.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<AiAssistPlugin>>();
            database.AddRange(groups.Select((group, index) => new AiUsage
            {
                Id = Guid.CreateVersion7(),
                SiteId = site,
                PrincipalKind = "apiKey",
                PrincipalId = group.Principal,
                PrincipalName = "Key",
                Action = group.Action,
                Provider = ScriptedFactory.Provider,
                Model = "test-model",
                PromptTokens = index + 1,
                CompletionTokens = 1,
                Succeeded = true,
                CreatedAt = _ai.Host.Time.GetUtcNow(),
            }));
            await database.SaveChangesAsync(Token);
        }

        var all = await UsageAsync(auditor, "?pageSize=1");
        var one = await UsageAsync(auditor, $"?principalId={principals[0]}");

        var expected = groups.Select((group, index) => ("apiKey", group.Principal, group.Action, 1L, index + 1L, 1L)).Skip(groups.Count - AiUsageService.MaxGroups).ToList();
        Groups(all).ShouldBe(InOrder(expected));
        all["totals"]!["groupsTruncated"]!.GetValue<bool>().ShouldBeTrue();
        Totals(all).ShouldBe((groups.Count, groups.Count * (groups.Count + 1) / 2, groups.Count));
        all["total"]!.GetValue<long>().ShouldBe(groups.Count);
        Groups(one).Count.ShouldBe(AiActions.Names.Count);
        one["totals"]!["groupsTruncated"]!.GetValue<bool>().ShouldBeFalse();
    }

    private static HttpClient InSite(HttpClient client, Guid site)
    {
        client.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());
        return client;
    }

    private static async Task<JsonNode> UsageAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri(Usage + query, UriKind.Relative), Token);
        return await JsonAsync(response);
    }

    private static (long Calls, long PromptTokens, long CompletionTokens) Totals(JsonNode page) =>
        (page["totals"]!["calls"]!.GetValue<long>(), page["totals"]!["promptTokens"]!.GetValue<long>(), page["totals"]!["completionTokens"]!.GetValue<long>());

    // By kind, id and action, as the answer orders them.
    private static List<(string Kind, Guid Principal, string Action, long Calls, long PromptTokens, long CompletionTokens)> InOrder(List<(string Kind, Guid Principal, string Action, long Calls, long PromptTokens, long CompletionTokens)> groups) =>
        [.. groups.OrderBy(group => group.Kind, StringComparer.Ordinal).ThenBy(group => group.Principal).ThenBy(group => group.Action, StringComparer.Ordinal)];

    private static List<(string Kind, Guid Principal, string Action, long Calls, long PromptTokens, long CompletionTokens)> Groups(JsonNode page) =>
        [.. page["totals"]!["groups"]!.AsArray().Select(group => (
            group!["principalKind"]!.GetValue<string>(),
            group["principalId"]!.GetValue<Guid>(),
            group["action"]!.GetValue<string>(),
            group["calls"]!.GetValue<long>(),
            group["promptTokens"]!.GetValue<long>(),
            group["completionTokens"]!.GetValue<long>()))];
}
