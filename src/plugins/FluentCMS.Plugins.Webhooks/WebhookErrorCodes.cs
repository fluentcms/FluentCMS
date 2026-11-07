namespace FluentCMS.Plugins.Webhooks;

// The webhooks plugin has the codes from FCMS1560 up to 1579. Each is added with AddErrorCode in WebhooksPlugin.
internal static class WebhookErrorCodes
{
    public const string InvalidField = "FCMS1560";
    public const string InvalidUrl = "FCMS1561";
    public const string PrivateAddress = "FCMS1562";
    public const string UnknownEvent = "FCMS1563";
    public const string NotFound = "FCMS1564";
    public const string DeliveryNotFound = "FCMS1565";
    public const string NameTaken = "FCMS1566";
}
