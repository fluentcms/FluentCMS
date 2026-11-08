namespace FluentCMS.Core.Events;

internal static class EventErrorCodes
{
    public const string DuplicateOutboxHandler = "FCMS2601";
    public const string PublishInForeignTransaction = "FCMS2602";
    public const string InvalidOutboxOptions = "FCMS2603";
    public const string DeliveryNotFound = "FCMS3138";
    public const string DeliveryNotFailed = "FCMS3139";
}
