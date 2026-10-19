namespace FluentCMS.Core.Webhooks;

// The events a subscription can ask for in M1 (API-05); others come with M2.
public static class WebhookEvents
{
    public const string ChangeSetPublished = "change_set.published";
    public const string ChangeSetReverted = "change_set.reverted";

    // Sent on request from the admin to check an endpoint; no subscription asks for it.
    public const string Test = "webhook.test";

    public static IReadOnlyList<string> All { get; } = [ChangeSetPublished, ChangeSetReverted];
}
