namespace FluentCMS.Core.Content;

internal static class ContentErrorCodes
{
    public const string SingletonExists = "FCMS2401";
    public const string NotPublished = "FCMS2402";
    public const string DraftMovedByHook = "FCMS2405";
    public const string NotScheduled = "FCMS2406";
    public const string ScheduleNotInFuture = "FCMS2407";
    public const string NonexistentLocalTime = "FCMS2408";
}
