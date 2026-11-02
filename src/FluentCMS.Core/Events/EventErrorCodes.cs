namespace FluentCMS.Core.Events;

internal static class EventErrorCodes
{
    public const string DuplicateOutboxHandler = "FCMS2601";
    public const string PublishInForeignTransaction = "FCMS2602";
    public const string InvalidOutboxOptions = "FCMS2603";
}
