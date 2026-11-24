namespace FluentCMS.Plugins.Webhooks.Admin;

// The addresses of the half's pages, relative to the admin's base.
internal static class WebhookRoutes
{
    public const string List = "webhooks";

    public static string Webhook(Guid id) => $"webhooks/{id:D}";
}
