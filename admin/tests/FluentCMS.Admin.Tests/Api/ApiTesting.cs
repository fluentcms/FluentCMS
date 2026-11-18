using System.Net;
using System.Text;
using FluentCMS.Admin.Api;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin.Tests.Api;

// The pipeline of AddManagementApi over a handler that answers what a test queues, with a session the test sets.
internal sealed class ApiHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public ApiHarness(string apiBaseUrl = "https://cms.example.com/")
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccessTokenSource>(Session);
        services.AddSingleton<ICurrentSite>(Session);
        services.AddSingleton<IContentLocale>(Session);
        services.AddSingleton<IRateLimitNotice>(Session);
        services.AddSingleton<TimeProvider>(Time);
        services.AddManagementApi();
        services.Configure<ManagementApiOptions>(options =>
        {
            options.ApiBaseUrl = new Uri(apiBaseUrl);
            options.PrimaryHandler = () => Server;
        });
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    public FakeSession Session { get; } = new();

    public ScriptedServer Server { get; } = new();

    public RecordingTime Time { get; } = new();

    public IManagementApi Api => _scope.ServiceProvider.GetRequiredService<IManagementApi>();

    public ManagementClient Client => _scope.ServiceProvider.GetRequiredService<ManagementClient>();

    public SiteMetadata Metadata => _scope.ServiceProvider.GetRequiredService<SiteMetadata>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

internal sealed class FakeSession : IAccessTokenSource, ICurrentSite, IContentLocale, IRateLimitNotice
{
    public string? Token { get; set; } = "token-1";

    public string? Site { get; set; }

    public string? Locale { get; set; }

    public List<TimeSpan?> Notices { get; } = [];

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Token);

    public ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public void TooManyRequests(TimeSpan? retryAfter) => Notices.Add(retryAfter);
}

internal sealed record SentRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

internal sealed class ScriptedServer : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<SentRequest> Requests { get; } = [];

    public ScriptedServer Answer(HttpStatusCode status, string? json = null, string mediaType = "application/json", Action<HttpResponseMessage>? configure = null)
    {
        _answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, mediaType);
            }

            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    public ScriptedServer Problem(HttpStatusCode status, string json, Action<HttpResponseMessage>? configure = null) =>
        Answer(status, json, "application/problem+json", configure);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(request.Method, request.RequestUri!, headers, body));
        if (_answers.Count == 0)
        {
            throw new InvalidOperationException($"No answer is queued for {request.Method} {request.RequestUri}.");
        }

        var response = _answers.Dequeue()();
        response.RequestMessage = request;
        return response;
    }
}

// Records each wait and lets it pass at once, so that a test of a 429 does not sleep.
internal sealed class RecordingTime : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    public List<TimeSpan> Waits { get; } = [];

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Waits.Add(dueTime);
        return new ImmediateTimer(callback, state);
    }

    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) => ThreadPool.QueueUserWorkItem(_ => callback(state));

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
