using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Data.Tests.Settings;

internal enum Quality
{
    Low,
    HighQuality,
}

[Settings("media")]
internal sealed class MediaSettings
{
    [Description("The largest upload, in megabytes.")]
    [Range(1, 500)]
    public int MaxUploadMb { get; set; } = 25;

    [Required]
    [StringLength(20)]
    [RegularExpression("^[a-z0-9]+$")]
    public string Provider { get; set; } = "local";

    public Quality Quality { get; set; } = Quality.HighQuality;

    public List<string> Tags { get; set; } = [];

    [SettingsSecret]
    public string? StorageAccount { get; set; } = "default-account-8841";

    // Secret by its name alone.
    public string? ApiKey { get; set; }

    [SettingsSecret]
    [StringLength(8)]
    public string? Passphrase { get; set; }
}

[Settings("seo")]
internal sealed class SeoSettings
{
    public string Title { get; set; } = "Untitled";
}

internal sealed class NoAttribute
{
    public string Name { get; set; } = "";
}

[Settings("Bad Area")]
internal sealed class BadArea
{
    public string Name { get; set; } = "";
}

[Settings("media")]
internal sealed class SecondMedia
{
    public string Name { get; set; } = "";
}

[Settings("empty")]
internal sealed class Empty;

internal sealed class Limits
{
    [Range(1, 10)]
    public int Port { get; set; } = 5;
}

// A property below the top level is not a secret, whatever its name, so its default is shown like any other.
internal sealed class Smtp
{
    public string Host { get; set; } = "smtp";

    public string? Token { get; set; } = "nested-token-default-52";
}

[Settings("nested")]
internal sealed class NestedSettings
{
    public Limits Limits { get; set; } = new();

    public Smtp Smtp { get; set; } = new();

    public List<Limits> Many { get; set; } = [];

    public Dictionary<string, Limits> ByName { get; set; } = [];

    [SettingsSecret]
    public string? Token { get; set; } = "root-token-default-31";
}

internal sealed class Inner
{
    [SettingsSecret]
    public string? Password { get; set; }
}

[Settings("bad-nested")]
internal sealed class SecretInAnObject
{
    public Inner Inner { get; set; } = new();
}

[Settings("bad-listed")]
internal sealed class SecretInAList
{
    public List<Inner> Inners { get; set; } = [];
}

internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public string All => string.Join('\n', _messages);

    public ILogger CreateLogger(string categoryName) => new Logger(_messages);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception) + exception);
    }
}

// The real service on a real SQLite file with sites A and B, the media and seo areas, and the activity log on.
internal sealed class SettingsDatabase : IDisposable
{
    public static readonly SiteId SiteA = ServiceDatabase.SiteA;
    public static readonly SiteId SiteB = ServiceDatabase.SiteB;

    public static readonly Principal Admin = AuthorizationDatabase.User([BuiltInRoles.SuperAdmin], [TokenScopes.Management]);

    public SettingsDatabase(Action<FluentCmsBuilder>? more = null)
    {
        Seen = new Seen();
        Logs = new CapturedLogs();
        Inner = new ServiceDatabase(ActivityFixture.On(cms =>
        {
            cms.Settings.Add<MediaSettings>().Add<SeoSettings>();
            cms.Services.AddSingleton<TimeProvider>(Clock);
            cms.Services.AddSingleton<ILoggerProvider>(Logs);
            cms.Services.AddSingleton(Seen);
            cms.Events.AddHandler<FluentCMS.Events.SettingsChangedEvent, ChangedHandler>();
            more?.Invoke(cms);
        }));
    }

    public static CancellationToken Token => ServiceDatabase.Token;

    public ServiceDatabase Inner { get; }

    public Seen Seen { get; }

    public CapturedLogs Logs { get; }

    public ManualTimeProvider Clock { get; } = new(JobFixture.Start);

    public static Principal UserWith(params Guid[] roles) => AuthorizationDatabase.User(roles, [TokenScopes.Management]);

    public Task<T> AsAsync<T>(Principal principal, Func<IServiceProvider, Task<T>> work, SiteId? site = null) =>
        Inner.AsAsync(principal, work, site: site);

    public Task<T> AsAsync<T>(Principal principal, Func<ISettings<MediaSettings>, Task<T>> work, SiteId? site = null) =>
        AsAsync(principal, services => work(services.GetRequiredService<ISettings<MediaSettings>>()), site);

    public async Task<MediaSettings> GetAsync(SiteId site) =>
        await AsAsync(Admin, async media => await media.GetAsync(site, Token));

    public async Task<MediaSettings> GetDefaultAsync() =>
        await AsAsync(Admin, async media => await media.GetDefaultAsync(Token));

    public Task SetAsync(SiteId site, MediaSettings value, Principal? principal = null) =>
        AsAsync(principal ?? Admin, async media =>
        {
            await media.SetAsync(site, value, Token);
            return true;
        });

    public Task SetDefaultAsync(MediaSettings value, Principal? principal = null) =>
        AsAsync(principal ?? Admin, async media =>
        {
            await media.SetDefaultAsync(value, Token);
            return true;
        });

    public Task<T> ServiceAsync<T>(Func<SettingsService, SettingsDescriptor, Task<T>> work, Principal? principal = null, SiteId? site = null) =>
        AsAsync(principal ?? Admin, async services =>
        {
            var service = services.GetRequiredService<SettingsService>();
            return await work(service, service.Require("media"));
        }, site);

    public List<SettingRow> Rows() =>
        Inner.Read(context => context.Set<SettingRow>().AsNoTracking().OrderBy(row => row.Key).ThenBy(row => row.SiteId).ToList());

    public void AddRow(Guid? site, string key, string json)
    {
        using var scope = Inner.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new SettingRow { Id = Guid.CreateVersion7(), SiteId = site, Key = key, Json = json, Version = 1, UpdatedAt = DateTimeOffset.UtcNow });
        context.SaveChanges();
    }

    public void EditRow(Guid? site, string key, string json)
    {
        using var scope = Inner.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Set<SettingRow>().Where(row => row.Key == key && row.SiteId == site).ExecuteUpdate(set => set.SetProperty(row => row.Json, json));
    }

    // Everything the change of a setting left outside the table: the outbox, the activity log and the log lines.
    public async Task<string> TracesAsync()
    {
        await Inner.DrainAsync();
        var outbox = Inner.Rows().Select(row => JsonSerializer.Serialize(row));
        var activity = Inner.ActivityRows().Select(row => JsonSerializer.Serialize(row));
        return string.Join('\n', outbox.Concat(activity).Append(Logs.All));
    }

    public void Dispose() => Inner.Dispose();
}

internal sealed class ChangedHandler(Seen seen) : FluentCMS.Events.IEventHandler<FluentCMS.Events.SettingsChangedEvent>
{
    public ValueTask HandleAsync(FluentCMS.Events.SettingsChangedEvent message, CancellationToken cancellationToken) =>
        seen.HeardAsync("settings", message, null, cancellationToken);
}
