using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

public sealed record WebhookRequest(
    string? Name,
    string? Url,
    IReadOnlyList<string>? Events,
    IReadOnlyList<string>? Types,
    IReadOnlyDictionary<string, string>? Headers,
    bool? Enabled,
    string? Secret);

public sealed record WebhookInfo(
    Guid Id,
    Guid SiteId,
    string Name,
    string Url,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Types,
    IReadOnlyDictionary<string, string> Headers,
    bool Enabled,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy)
{
    // Only in the answer of a create that generated the secret.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Secret { get; init; }

    // While the secret that the last rotation replaced still signs deliveries.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? PreviousSecretExpiresAt { get; init; }
}

public sealed record DeliveryInfo(
    Guid Id,
    Guid DeliveryId,
    Guid EventId,
    string Event,
    int Attempt,
    string State,
    int? StatusCode,
    long DurationMs,
    string Request,
    string? Response,
    string? Error,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset CreatedAt);

public sealed record DeliveryPage(IReadOnlyList<DeliveryInfo> Items, long Total, int Page, int PageSize);

// The webhooks of the site of the current operation. Every call needs webhooks.manage in that site; a webhook of another
// site is not found. Header values are secrets like the webhook's own: no answer holds them, the one to a create included.
internal sealed partial class WebhookService(
    PluginDbContext<WebhooksPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    WebhookSigning signing,
    IJobScheduler scheduler,
    IEventBus events,
    SchemaModel schema,
    IOptions<WebhooksOptions> options,
    IHostEnvironment environment,
    TimeProvider time)
{
    public const int MaxPageSize = 100;
    public const int MaxHeaders = 20;
    public const int HeaderValueMaxLength = 1024;

    // What an answer gives for the value of every header. Sent back in a PUT, it keeps the value stored under that name.
    public const string RedactedValue = "***";

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Type", "Content-Length", "Content-Encoding", "Transfer-Encoding", "Connection", "Upgrade", "Expect", "TE", "Trailer", "Keep-Alive", "Proxy-Authorization", "Proxy-Connection", "User-Agent",
    };

    public async Task<IReadOnlyList<WebhookInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var webhooks = await database.Set<Webhook>().AsNoTracking().Where(webhook => webhook.SiteId == site.Value).ToListAsync(cancellationToken);
        return [.. webhooks.OrderBy(webhook => webhook.Name, StringComparer.Ordinal).Select(Info)];
    }

    public async Task<WebhookInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        return Info(await FindAsync(site, id, tracked: false, cancellationToken));
    }

    public async Task<WebhookInfo> CreateAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await RequireAsync(cancellationToken);
        var valid = await ValidateAsync(request, new Dictionary<string, string>(), cancellationToken);
        var generated = request.Secret is null ? WebhookSigning.NewSecret() : null;
        var actor = Operation().AuditPrincipal.Id;
        var now = time.GetUtcNow();
        var webhook = new Webhook
        {
            Id = Guid.CreateVersion7(),
            SiteId = site.Value,
            Secret = signing.Protect(request.Secret ?? generated!),
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
        };
        valid.ApplyTo(webhook, signing);

        database.Add(webhook);
        await SaveAsync(site, webhook, cancellationToken);
        await events.PublishAsync(new WebhookCreatedEvent(webhook.Id, webhook.Name), cancellationToken);
        return Info(webhook) with { Secret = generated };
    }

    // The whole webhook; a request without a secret keeps the one that is stored. A new secret keeps the one it replaces
    // signing beside it for SecretOverlap, and every write drops a replaced secret whose overlap has ended. Writing
    // protects headers that a row from before their protection holds in plain JSON.
    public async Task<WebhookInfo> UpdateAsync(Guid id, WebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await RequireAsync(cancellationToken);
        var webhook = await FindAsync(site, id, tracked: true, cancellationToken);
        var valid = await ValidateAsync(request, signing.TryUnprotectHeaders(webhook.Headers) ?? new Dictionary<string, string>(), cancellationToken);
        valid.ApplyTo(webhook, signing);

        var now = time.GetUtcNow();
        if (webhook.PreviousSecretExpiresAt <= now)
        {
            webhook.PreviousSecret = null;
            webhook.PreviousSecretExpiresAt = null;
        }

        if (request.Secret is not null)
        {
            var overlap = options.Value.SecretOverlap;
            webhook.PreviousSecret = overlap > TimeSpan.Zero ? webhook.Secret : null;
            webhook.PreviousSecretExpiresAt = overlap > TimeSpan.Zero ? now + overlap : null;
            webhook.Secret = signing.Protect(request.Secret);
        }

        webhook.UpdatedAt = now;
        webhook.UpdatedBy = Operation().AuditPrincipal.Id;

        await SaveAsync(site, webhook, cancellationToken);
        await events.PublishAsync(new WebhookUpdatedEvent(webhook.Id, webhook.Name, request.Secret is not null), cancellationToken);
        return Info(webhook);
    }

    // The webhook, then its delivery log. A retry already scheduled finds no webhook and sends nothing; an attempt that was
    // running as the log went is left to the retention job.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var webhook = await FindAsync(site, id, tracked: false, cancellationToken);

        await database.Set<Webhook>().Where(item => item.Id == webhook.Id).ExecuteDeleteAsync(cancellationToken);
        await database.Set<WebhookDelivery>().Where(delivery => delivery.WebhookId == webhook.Id).ExecuteDeleteAsync(cancellationToken);
        await events.PublishAsync(new WebhookDeletedEvent(webhook.Id, webhook.Name), cancellationToken);
    }

    // The attempts of the webhook's deliveries, newest first.
    public async Task<DeliveryPage> DeliveriesAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw Invalid("pageSize", $"The page size is from 1 to {MaxPageSize}.");
        }

        if (page < 1 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw Invalid("page", "The page number starts at 1 and is not past the end of every log.");
        }

        var webhook = await FindAsync(site, id, tracked: false, cancellationToken);
        var query = database.Set<WebhookDelivery>().AsNoTracking().Where(delivery => delivery.WebhookId == webhook.Id);
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(delivery => delivery.CreatedAt).ThenByDescending(delivery => delivery.Attempt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new DeliveryPage([.. rows.Select(Info)], total, page, pageSize);
    }

    // Sends the body of a delivery again, once, as a new delivery.
    public async Task<Guid> RedeliverAsync(Guid id, Guid deliveryId, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var webhook = await FindAsync(site, id, tracked: false, cancellationToken);
        var sent = await database.Set<WebhookDelivery>().AsNoTracking()
            .Where(delivery => delivery.WebhookId == webhook.Id && delivery.DeliveryId == deliveryId)
            .OrderBy(delivery => delivery.Attempt)
            .Select(delivery => new { delivery.EventId, delivery.Event, delivery.RequestBody })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PluginException(WebhookErrorCodes.DeliveryNotFound, $"The webhook {id:D} has no delivery {deliveryId:D}.");

        return await EnqueueAsync(webhook, sent.EventId, sent.Event, sent.RequestBody, cancellationToken);
    }

    // Sends a ping event, once.
    public async Task<Guid> PingAsync(Guid id, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var webhook = await FindAsync(site, id, tracked: false, cancellationToken);
        var eventId = Guid.CreateVersion7();
        return await EnqueueAsync(webhook, eventId, WebhookEvents.Ping, WebhookPayload.Ping(eventId, time.GetUtcNow(), Operation()), cancellationToken);
    }

    private async Task<Guid> EnqueueAsync(Webhook webhook, Guid eventId, string name, string body, CancellationToken cancellationToken)
    {
        var deliveryId = Guid.CreateVersion7();
        var attempt = new WebhookAttempt(webhook.Id, deliveryId, eventId, name, 1, Manual: true, body);
        await scheduler.EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(attempt, WebhookDeliveryJob.Options(webhook.SiteId), cancellationToken);
        return deliveryId;
    }

    private OperationContext Operation() =>
        operations.Current ?? throw new InvalidOperationException("The webhooks of a site are managed inside an operation, which names the site.");

    private async Task<SiteId> RequireAsync(CancellationToken cancellationToken)
    {
        var site = Operation().Site;
        await permissions.RequireAsync(WebhookPermissions.Manage, site, cancellationToken);
        return site;
    }

    private async Task<Webhook> FindAsync(SiteId site, Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var webhooks = tracked ? database.Set<Webhook>() : database.Set<Webhook>().AsNoTracking();
        return await webhooks.FirstOrDefaultAsync(webhook => webhook.Id == id && webhook.SiteId == site.Value, cancellationToken)
            ?? throw new PluginException(WebhookErrorCodes.NotFound, $"The site has no webhook {id:D}.");
    }

    // Two writers that both found the name free meet at the unique index.
    private async Task SaveAsync(SiteId site, Webhook webhook, CancellationToken cancellationToken)
    {
        if (await NameTakenAsync(site, webhook, cancellationToken))
        {
            throw NameTaken(webhook.Name);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await NameTakenAsync(site, webhook, CancellationToken.None))
            {
                throw NameTaken(webhook.Name);
            }

            throw;
        }
    }

    private Task<bool> NameTakenAsync(SiteId site, Webhook webhook, CancellationToken cancellationToken) =>
        database.Set<Webhook>().AsNoTracking().AnyAsync(item => item.SiteId == site.Value && item.Name == webhook.Name && item.Id != webhook.Id, cancellationToken);

    private static PluginException NameTaken(string name) => new(WebhookErrorCodes.NameTaken, $"The site has a webhook named '{name}'.");

    // Stored holds the headers of the webhook being replaced, whose values a redacted value keeps.
    private async Task<ValidRequest> ValidateAsync(WebhookRequest request, IReadOnlyDictionary<string, string> stored, CancellationToken cancellationToken)
    {
        List<ValidationError> errors = [];
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > WebhookModel.NameMaxLength)
        {
            errors.Add(new("name", WebhookErrorCodes.InvalidField, $"A name is required, of at most {WebhookModel.NameMaxLength} characters."));
        }

        var url = request.Url?.Trim();
        if (string.IsNullOrEmpty(url) || url.Length > WebhookModel.UrlMaxLength)
        {
            errors.Add(new("url", WebhookErrorCodes.InvalidUrl, $"A URL is required, of at most {WebhookModel.UrlMaxLength} characters."));
        }
        else if (WebhookAddresses.UrlRefusal(url, environment.IsDevelopment()) is { } refusal)
        {
            errors.Add(new("url", WebhookErrorCodes.InvalidUrl, refusal));
        }
        else if (!options.Value.AllowPrivateNetworks && await WebhookAddresses.AddressRefusalAsync(new Uri(url), cancellationToken) is { } notPublic)
        {
            errors.Add(new("url", WebhookErrorCodes.PrivateAddress, notPublic));
        }

        var subscribed = request.Events ?? [];
        if (subscribed.Count == 0)
        {
            errors.Add(new("events", WebhookErrorCodes.UnknownEvent, $"Name one or more events: {string.Join(", ", WebhookEvents.Subscribable.Order(StringComparer.Ordinal))}."));
        }

        for (var index = 0; index < subscribed.Count; index++)
        {
            if (subscribed[index] is not { } item || !WebhookEvents.Subscribable.Contains(item))
            {
                errors.Add(new($"events[{index}]", WebhookErrorCodes.UnknownEvent, $"'{subscribed[index]}' is not an event a webhook can subscribe to."));
            }
        }

        var types = request.Types ?? [];
        for (var index = 0; index < types.Count; index++)
        {
            if (types[index] is not { } item || !schema.ContentTypes.Any(type => type.Name == item))
            {
                errors.Add(new($"types[{index}]", WebhookErrorCodes.InvalidField, $"'{types[index]}' is not a content type of the schema."));
            }
        }

        var headers = request.Headers ?? new Dictionary<string, string>();
        if (headers.Count > MaxHeaders)
        {
            errors.Add(new("headers", WebhookErrorCodes.InvalidField, $"A webhook has at most {MaxHeaders} headers."));
        }

        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, given) in headers)
        {
            var value = given == RedactedValue && stored.FirstOrDefault(pair => string.Equals(pair.Key, header, StringComparison.OrdinalIgnoreCase)).Value is { } kept ? kept : given;
            var problem = !HeaderName().IsMatch(header) ? "is not a header name"
                : !named.TryAdd(header, value) ? "is named twice"
                : ReservedHeaders.Contains(header) || header.StartsWith("X-FluentCMS-", StringComparison.OrdinalIgnoreCase) ? "is set by the delivery"
                : value == RedactedValue ? $"has no stored value for '{RedactedValue}' to keep, so it needs its value"
                : value is null || value.Length > HeaderValueMaxLength || value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0 ? $"needs a value of at most {HeaderValueMaxLength} characters on one line"
                : null;
            if (problem is not null)
            {
                errors.Add(new($"headers.{header}", WebhookErrorCodes.InvalidField, $"The header '{header}' {problem}."));
            }
        }

        if (request.Secret is { } secret && (secret.Length < WebhookSigning.SecretMinLength || secret.Length > WebhookSigning.SecretMaxLength || string.IsNullOrWhiteSpace(secret)))
        {
            errors.Add(new("secret", WebhookErrorCodes.InvalidField, $"A secret has from {WebhookSigning.SecretMinLength} to {WebhookSigning.SecretMaxLength} characters."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new ValidRequest(name!, url!, [.. subscribed.Distinct(StringComparer.Ordinal)], [.. types.Distinct(StringComparer.Ordinal)], named, request.Enabled ?? true);
    }

    private static ValidationException Invalid(string path, string message) => new([new ValidationError(path, WebhookErrorCodes.InvalidField, message)]);

    // The names of the headers, each with the redacted value; none when the key ring cannot read them, as a delivery then
    // reports.
    private WebhookInfo Info(Webhook webhook) => new(
        webhook.Id,
        webhook.SiteId,
        webhook.Name,
        webhook.Url,
        WebhookJson.List(webhook.Events),
        WebhookJson.List(webhook.Types),
        (signing.TryUnprotectHeaders(webhook.Headers) ?? new Dictionary<string, string>()).ToDictionary(header => header.Key, _ => RedactedValue),
        webhook.Enabled,
        webhook.CreatedAt,
        webhook.CreatedBy,
        webhook.UpdatedAt,
        webhook.UpdatedBy)
    {
        PreviousSecretExpiresAt = webhook.PreviousSecretExpiresAt > time.GetUtcNow() ? webhook.PreviousSecretExpiresAt : null,
    };

    private static DeliveryInfo Info(WebhookDelivery delivery) => new(
        delivery.Id,
        delivery.DeliveryId,
        delivery.EventId,
        delivery.Event,
        delivery.Attempt,
        delivery.State,
        delivery.StatusCode,
        delivery.DurationMs,
        delivery.RequestBody,
        delivery.ResponseExcerpt,
        delivery.Error,
        delivery.NextRetryAt,
        delivery.CreatedAt);

    // RFC 9110 token characters.
    [GeneratedRegex(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]{1,100}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderName();

    private sealed record ValidRequest(string Name, string Url, List<string> Events, List<string> Types, Dictionary<string, string> Headers, bool Enabled)
    {
        public void ApplyTo(Webhook webhook, WebhookSigning signing)
        {
            webhook.Name = Name;
            webhook.Url = Url;
            webhook.Events = WebhookJson.Of(Events);
            webhook.Types = WebhookJson.Of(Types);
            webhook.Headers = signing.ProtectHeaders(Headers);
            webhook.Enabled = Enabled;
        }
    }
}
