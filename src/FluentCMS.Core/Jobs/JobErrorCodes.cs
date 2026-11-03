namespace FluentCMS.Core.Jobs;

internal static class JobErrorCodes
{
    public const string UnknownJobType = "FCMS2501";
    public const string BadPayload = "FCMS2502";
    public const string InvalidCron = "FCMS2503";
    public const string UnregisteredJob = "FCMS2504";
    public const string DuplicateJobType = "FCMS2505";
    public const string InvalidRunnerOptions = "FCMS2506";
    public const string BadActor = "FCMS2507";
    public const string AttemptTimedOut = "FCMS2508";
}
