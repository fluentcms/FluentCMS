using FluentCMS.Activity;
using FluentCMS.Diagnostics;
using FluentCMS.Events;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Webhooks;
using Microsoft.Extensions.DependencyInjection;

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
            .Validate(options => options.Timeout > TimeSpan.Zero && options.Retention > TimeSpan.Zero, $"{WebhooksOptions.Section}: Timeout and Retention must be positive.");
        plugin.Services.AddSingleton<WebhookSender>();
        plugin.Services.AddSingleton<WebhookSigning>();
        plugin.Services.AddScoped<WebhookService>();
        plugin.Services.AddScoped<WebhookDispatcher>();
        plugin.Services.AddSingleton<IInsecureDefaultSource, WebhookInsecureDefaults>();
        plugin.Services.AddHostedService<WebhookRetentionSchedule>();

        plugin
            .AddPermissions<WebhookPermissions>()
            .AddModel<WebhookModel>()
            .AddMigrations(typeof(WebhooksPlugin).Assembly)
            .AddJob<WebhookDeliveryJob, WebhookAttempt>(WebhookDeliveryJob.Type)
            .AddJob<WebhookRetentionJob, WebhookRetentionPayload>(WebhookRetentionJob.Type)
            .AddErrorCode(WebhookErrorCodes.InvalidField, 422, "Invalid webhook")
            .AddErrorCode(WebhookErrorCodes.InvalidUrl, 422, "Invalid webhook URL")
            .AddErrorCode(WebhookErrorCodes.PrivateAddress, 422, "Webhook URL is not public")
            .AddErrorCode(WebhookErrorCodes.UnknownEvent, 422, "Unknown webhook event")
            .AddErrorCode(WebhookErrorCodes.NotFound, 404, "Webhook not found")
            .AddErrorCode(WebhookErrorCodes.DeliveryNotFound, 404, "Webhook delivery not found")
            .AddErrorCode(WebhookErrorCodes.NameTaken, 409, "Webhook name taken")
            .AddActivity<WebhookCreatedEvent>("webhook.created", created => Activity("webhook.created", created.WebhookId, created.Name, null))
            .AddActivity<WebhookUpdatedEvent>("webhook.updated", updated => Activity("webhook.updated", updated.WebhookId, updated.Name, updated.SecretChanged))
            .AddActivity<WebhookDeletedEvent>("webhook.deleted", deleted => Activity("webhook.deleted", deleted.WebhookId, deleted.Name, null));

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
    }

    public void Configure(IPluginAppBuilder app) => app.MapManagement("/webhooks").MapWebhookEndpoints();

    private static void Subscribe<TEvent>(IPluginServiceBuilder plugin)
        where TEvent : CmsEvent =>
        plugin.AddOutboxHandler<TEvent, WebhookEventHandler<TEvent>>("webhooks." + WebhookEvents.Names[typeof(TEvent)]);

    private static ActivityEntry Activity(string action, Guid webhookId, string name, bool? secretChanged) => new(action)
    {
        TargetType = "webhook",
        TargetId = webhookId.ToString("D"),
        TargetLabel = name,
        Details = secretChanged is { } changed ? new Dictionary<string, string?> { ["secretChanged"] = changed ? "true" : "false" } : null,
    };
}
