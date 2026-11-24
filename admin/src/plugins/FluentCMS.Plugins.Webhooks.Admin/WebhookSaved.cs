using FluentCMS.Plugins.Webhooks.Admin.Api;

namespace FluentCMS.Plugins.Webhooks.Admin;

// A save of WebhookForm: the server's answer, and the secret to show once, which the page holds until its dialog closes.
public sealed record WebhookSaved(WebhookRecord Webhook, string? Secret, bool Created);
