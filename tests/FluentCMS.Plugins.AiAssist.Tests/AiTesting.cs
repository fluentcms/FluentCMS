using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Plugins.Media;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using SkiaSharp;
using Xunit;
using Role = FluentCMS.Core.Storage.Entities.Role;
using RoleGrant = FluentCMS.Core.Storage.Entities.RoleGrant;
using SettingRow = FluentCMS.Core.Storage.Entities.SettingRow;

namespace FluentCMS.Plugins.AiAssist.Tests;

// A host with the AI assist and media plugins, both discovered, on one content type. Scripted, the IAiClientFactory slot
// is the host's own and hands out one scripted chat client, so no test calls a provider; otherwise the slot keeps the
// plugin's default. The tests of a class share a host, each in a site of its own, with its settings, callers and usage.
public sealed class AiHost : IAsyncDisposable
{
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-ai-");
    private readonly ConcurrentDictionary<HttpClient, Guid> _principals = new();

    public AiHost()
        : this(scripted: true)
    {
    }

    // At LogLevel.Trace the log holds every line of the host, the debug lines of the framework and the packages included.
    internal AiHost(bool scripted, Action<AiOptions>? configure = null, LogLevel minimumLevel = LogLevel.Information)
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes<Article>()
            .WithServices(services =>
            {
                services.AddSingleton(Chat);
                services.AddSingleton<ILoggerProvider>(Logs);
                services.AddLogging(logging => logging.SetMinimumLevel(minimumLevel));
                services.PostConfigure<MediaOptions>(options => options.Local.Root = _media.FullName);
                if (configure is not null)
                {
                    services.PostConfigure(configure);
                }
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                if (scripted)
                {
                    cms.Providers.Use<IAiClientFactory, ScriptedFactory>();
                }
            });
    }

    public FluentCmsTestHost Host { get; }

    public ScriptedChat Chat { get; } = new();

    public LogCapture Logs { get; } = new();

    public IServiceProvider Services => Host.Services;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A site with these locales after "en", whose settings name a provider and opt in the title, summary and body of
    // an article, changed as the test asks.
    public async Task<Guid> SiteAsync(Action<AiSettings>? change = null, params string[] locales)
    {
        var site = await BareSiteAsync(locales);
        var settings = new AiSettings { Provider = AiProvider.OpenAi, Model = "test-model", Fields = { ["article"] = ["title", "summary", "body"] } };
        change?.Invoke(settings);
        await SetSettingsAsync(site, settings);
        return site;
    }

    // A site with no settings of its own, which follows the instance defaults.
    public async Task<Guid> BareSiteAsync(params string[] locales)
    {
        await using var scope = Services.CreateAsyncScope();
        var sites = scope.ServiceProvider.GetRequiredService<SiteManager>();
        var site = (await sites.CreateAsync("Docs", $"docs-{Guid.NewGuid():N}", "en", Token)).Id;
        foreach (var locale in locales)
        {
            await sites.EnsureLocaleAsync(site, locale, Token);
        }

        return site;
    }

    public async Task SetDefaultsAsync(AiSettings settings)
    {
        await using var scope = Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<ISettings<AiSettings>>().SetDefaultAsync(settings, Token);
    }

    // As the host's own code, which a settings write takes as allowed.
    public async Task SetSettingsAsync(Guid site, AiSettings settings)
    {
        await using var scope = Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(site));
        await scope.ServiceProvider.GetRequiredService<ISettings<AiSettings>>().SetAsync(new SiteId(site), settings, Token);
    }

    // Settings of a site as an earlier version of the plugin stored them, written past the settings service, which would
    // refuse them now.
    public async Task StoreSettingsAsync(Guid site, string json)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new SettingRow { Id = Guid.CreateVersion7(), SiteId = site, Key = "ai", Json = json, Version = 1, UpdatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync(Token);
    }

    public async Task SetEnabledAsync(string plugin, Guid site, bool enabled)
    {
        await using var scope = Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<PluginAdministration>().SetEnabledAsync(plugin, site, enabled, Token);
    }

    // A Management key of Super Admin whose every request names the site.
    public Task<HttpClient> AdminAsync(Guid site) => KeyAsync(site, BuiltInRoles.SuperAdminName);

    // A Management key of a new instance-wide role with these grants, each a permission and its constraints, if any.
    public async Task<HttpClient> CallerAsync(Guid site, params (string Permission, PermissionConstraints? Constraints)[] grants)
    {
        var role = new Role { Id = Guid.CreateVersion7(), Name = $"role-{Guid.NewGuid():N}" };
        await using (var scope = Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<Role>().Add(role);
            context.Set<RoleGrant>().AddRange(grants.Select(grant => new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = grant.Permission, Constraints = grant.Constraints?.ToJson() }));
            await context.SaveChangesAsync(Token);
        }

        return await KeyAsync(site, role.Name);
    }

    // The id of the principal of a client this host made.
    public Guid PrincipalOf(HttpClient client) => _principals[client];

    // The usage rows of the site, oldest first.
    internal async Task<List<AiUsage>> UsageAsync(Guid site)
    {
        await using var scope = Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<PluginDbContext<AiAssistPlugin>>().Set<AiUsage>().AsNoTracking().Where(usage => usage.SiteId == site).ToListAsync(Token);
        return [.. rows.OrderBy(usage => usage.CreatedAt).ThenBy(usage => usage.Id)];
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        try
        {
            _media.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<HttpClient> KeyAsync(Guid site, string role)
    {
        var key = await Host.CreateApiKeyAsync(ApiKeyClass.Management, role, cancellationToken: Token);
        var client = Host.AsKey(key);
        client.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());
        _principals[client] = key.Principal.Id;
        return client;
    }
}

internal static class AiTesting
{
    public const string Actions = "/api/management/v1/ai/actions/";
    public const string Usage = "/api/management/v1/ai/usage";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<HttpResponseMessage> ActAsync(HttpClient client, string action, object body, bool stream = false, CancellationToken? cancellationToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Actions + action, UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (stream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        return await client.SendAsync(request, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, cancellationToken ?? Token);
    }

    // The body of an answer with the status expected.
    public static async Task<JsonNode> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(expected, text);
        return JsonNode.Parse(text)!;
    }

    // The code of a refusal with the status expected.
    public static async Task<string> RefusedAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        (await JsonAsync(response, expected))["code"]!.GetValue<string>();

    public static async Task<JsonNode> SuggestAsync(HttpClient client, string action, object body)
    {
        using var response = await ActAsync(client, action, body);
        return await JsonAsync(response);
    }

    // The events of a text/event-stream body, in order.
    public static List<(string Event, JsonNode Data)> StreamEvents(string body) =>
        [.. body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(block =>
        {
            var lines = block.Split('\n');
            return (lines[0]["event: ".Length..], JsonNode.Parse(lines[1]["data: ".Length..])!);
        })];

    // A draft of an article in the site and locale of the client, and its id.
    public static async Task<Guid> ArticleAsync(HttpClient client, object fields, string? locale = null)
    {
        var path = "/api/management/v1/content/article" + (locale is null ? "" : $"?locale={locale}");
        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), fields, Token);
        return Guid.Parse((await JsonAsync(response, HttpStatusCode.Created))["id"]!.GetValue<string>());
    }

    public static string Text(ChatMessage message) => string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    // A file uploaded to the media library of the site of the client, and its id.
    public static async Task<Guid> UploadAsync(HttpClient client, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", name);
        using var response = await client.PostAsync(new Uri("/api/management/v1/media", UriKind.Relative), form, Token);
        return Guid.Parse((await JsonAsync(response, HttpStatusCode.Created))["items"]![0]!["id"]!.GetValue<string>());
    }

    // A PNG of one colour.
    public static byte[] Picture(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(20, 90, 160));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}

// What the scripted client was asked.
public sealed record ScriptedCall(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options, bool Streamed);

// The chat client of the tests: it answers what a test scripted and records what it was asked. A streamed answer comes
// in Pieces, with the usage in two parts, the prompt before the first piece and the completion after the last, as some
// providers send it.
public sealed class ScriptedChat : IChatClient
{
    private readonly ConcurrentQueue<ScriptedCall> _calls = new();
    private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // What the host's factory names as the provider of this client.
    public string Provider { get; set; } = ScriptedFactory.Provider;

    public string Answer { get; set; } = "A suggestion.";

    public IReadOnlyList<string>? Pieces { get; set; }

    public long PromptTokens { get; set; } = 12;

    public long CompletionTokens { get; set; } = 5;

    // Thrown in place of an answer, or for a streamed answer after its first piece.
    public Exception? Failure { get; set; }

    // Keeps the call open until it is cancelled, for a streamed answer after its first piece.
    public bool Hang { get; set; }

    public IReadOnlyList<ScriptedCall> Calls => [.. _calls];

    // Completes when a call has arrived.
    public Task Entered => _entered.Task;

    public void Reset()
    {
        _calls.Clear();
        _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        (Answer, Pieces, PromptTokens, CompletionTokens, Failure, Hang) = ("A suggestion.", null, 12, 5, null, false);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Record(messages, options, streamed: false);
        await FailOrHangAsync(cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)) { Usage = Used() };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Record(messages, options, streamed: true);
        var pieces = Pieces ?? [Answer];
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = PromptTokens })]);
        for (var index = 0; index < pieces.Count; index++)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, pieces[index]);
            if (index == 0)
            {
                await FailOrHangAsync(cancellationToken);
            }
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { OutputTokenCount = CompletionTokens })]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private void Record(IEnumerable<ChatMessage> messages, ChatOptions? options, bool streamed)
    {
        _calls.Enqueue(new ScriptedCall([.. messages], options, streamed));
        _entered.TrySetResult();
    }

    private async Task FailOrHangAsync(CancellationToken cancellationToken)
    {
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }

    private UsageDetails Used() => new() { InputTokenCount = PromptTokens, OutputTokenCount = CompletionTokens, TotalTokenCount = PromptTokens + CompletionTokens };
}

// A host's own factory: every site that names a provider gets the scripted client, under the model its settings name.
internal sealed class ScriptedFactory(ScriptedChat chat) : IAiClientFactory
{
    public const string Provider = "scripted";

    public ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(settings.Provider == AiProvider.None ? null : new AiClient(chat, chat.Provider, settings.Model ?? "scripted-model"));
}

public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<(int Id, LogLevel Level, string Line)> _lines = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _waiting = new();

    public IReadOnlyList<string> Lines => [.. _lines.Select(line => line.Line)];

    public IReadOnlyList<string> Of(int eventId) => [.. _lines.Where(line => line.Id == eventId).Select(line => line.Line)];

    public IReadOnlyList<string> At(LogLevel level) => [.. _lines.Where(line => line.Level == level).Select(line => line.Line)];

    // Completes when the next line with the event id is written.
    public Task NextAsync(int eventId) => _waiting.GetOrAdd(eventId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            capture._lines.Enqueue((eventId.Id, logLevel, $"{eventId.Id} {formatter(state, exception)} {exception}"));
            if (capture._waiting.TryRemove(eventId.Id, out var waiting))
            {
                waiting.TrySetResult();
            }
        }
    }
}
