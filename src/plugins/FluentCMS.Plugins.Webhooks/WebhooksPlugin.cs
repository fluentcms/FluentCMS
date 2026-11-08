using System.Globalization;
using FluentCMS.Activity;
using FluentCMS.Diagnostics;
using FluentCMS.Events;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

[assembly: FluentCmsPlugin(typeof(WebhooksPlugin))]

namespace FluentCMS.Plugins.Webhooks;

[FluentCmsPlugin("webhooks", SdkRange = "[1.0.0,2.0.0)")]
public sealed class WebhooksPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddOptions<WebhooksOptions>()
            .BindConfiguration(WebhooksOptions.Section)
            .Validate(
                options => options.Timeout > TimeSpan.Zero && options.Timeout <= TimeSpan.FromMinutes(5) && options.Retention > TimeSpan.Zero && options.Workers >= 0 && options.SecretOverlap >= TimeSpan.Zero
                    && (options.DisableAfter is null || (options.DisableAfter > TimeSpan.Zero && options.DisableAfter <= options.Retention)),
                $"{WebhooksOptions.Section}: Timeout must be positive and at most five minutes, Retention positive, Workers and SecretOverlap not negative, and DisableAfter, when set, positive and at most Retention.");
        plugin.Services.AddSingleton<WebhookSender>();
        plugin.Services.AddSingleton<WebhookSigning>();
        plugin.Services.AddScoped<WebhookService>();
        plugin.Services.AddScoped<WebhookDispatcher>();
        plugin.Services.AddSingleton<IInsecureDefaultSource, WebhookInsecureDefaults>();
        plugin.Services.AddHostedService<WebhookStartup>();

        plugin
            .AddPermissions<WebhookPermissions>()
            .AddModel<WebhookModel>()
            .AddMigrations(typeof(WebhooksPlugin).Assembly)
            .AddJob<WebhookDeliveryJob, WebhookAttempt>(WebhookDeliveryJob.Type)
            .AddJobQueue(WebhookDeliveryJob.Queue, services => services.GetRequiredService<IOptions<WebhooksOptions>>().Value.Workers)
            .AddJob<WebhookRetentionJob, WebhookRetentionPayload>(WebhookRetentionJob.Type, WebhookRetentionJob.AttemptTimeout)
            .AddErrorCode(WebhookErrorCodes.InvalidField, 422, "Invalid webhook")
            .AddErrorCode(WebhookErrorCodes.InvalidUrl, 422, "Invalid webhook URL")
            .AddErrorCode(WebhookErrorCodes.PrivateAddress, 422, "Webhook URL is not public")
            .AddErrorCode(WebhookErrorCodes.UnknownEvent, 422, "Unknown webhook event")
            .AddErrorCode(WebhookErrorCodes.NotFound, 404, "Webhook not found")
            .AddErrorCode(WebhookErrorCodes.DeliveryNotFound, 404, "Webhook delivery not found")
            .AddErrorCode(WebhookErrorCodes.NameTaken, 409, "Webhook name taken")
            .AddActivity<WebhookCreatedEvent>("webhook.created", created => Activity("webhook.created", created.WebhookId, created.Name))
            .AddActivity<WebhookUpdatedEvent>("webhook.updated", updated => Activity("webhook.updated", updated.WebhookId, updated.Name, new() { ["secretChanged"] = updated.SecretChanged ? "true" : "false" }))
            .AddActivity<WebhookDeletedEvent>("webhook.deleted", deleted => Activity("webhook.deleted", deleted.WebhookId, deleted.Name))
            .AddActivity<WebhookDeadEvent>("webhook.delivery_dead", dead => Activity("webhook.delivery_dead", dead.WebhookId, dead.Name, new()
            {
                ["deliveryId"] = dead.DeliveryId.ToString("D"),
                ["event"] = dead.Event,
                ["attempts"] = dead.Attempts.ToString(CultureInfo.InvariantCulture),
            }))
            .AddActivity<WebhookDisabledEvent>("webhook.disabled", disabled => Activity("webhook.disabled", disabled.WebhookId, disabled.Name, new() { ["failingSince"] = disabled.FailingSince.ToString("O", CultureInfo.InvariantCulture) }));

        Subscribe<EntryDraftSavedEvent>(plugin);
        Subscribe<EntryPublishedEvent>(plugin);
        Subscribe<EntryUnpublishedEvent>(plugin);
        Subscribe<EntryDraftDiscardedEvent>(plugin);
        Subscribe<EntryVersionRestoredEvent>(plugin);
        Subscribe<EntryTrashedEvent>(plugin);
        Subscribe<EntryRestoredEvent>(plugin);
        Subscribe<EntryPurgedEvent>(plugin);
        Subscribe<EntryScheduledEvent>(plugin);
        Subscribe<EntryScheduleCancelledEvent>(plugin);
        Subscribe<CacheInvalidatedEvent>(plugin);
        Subscribe<MediaAssetUploadedEvent>(plugin);
        Subscribe<MediaAssetUpdatedEvent>(plugin);
        Subscribe<MediaAssetReplacedEvent>(plugin);
        Subscribe<MediaAssetMovedEvent>(plugin);
        Subscribe<MediaAssetDeletedEvent>(plugin);
    }

    public void Configure(IPluginAppBuilder app) => app.MapManagement("/webhooks").MapWebhookEndpoints();

    private static void Subscribe<TEvent>(IPluginServiceBuilder plugin)
        where TEvent : CmsEvent =>
        plugin.AddOutboxHandler<TEvent, WebhookEventHandler<TEvent>>("webhooks." + WebhookEvents.Names[typeof(TEvent)]);

    private static ActivityEntry Activity(string action, Guid webhookId, string name, Dictionary<string, string?>? details = null) => new(action)
    {
        TargetType = "webhook",
        TargetId = webhookId.ToString("D"),
        TargetLabel = name,
        Details = details,
    };
}
