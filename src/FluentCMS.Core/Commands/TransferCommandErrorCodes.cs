namespace FluentCMS.Core.Commands;

// The commands export, import, backup and restore, from 3240 up to 3259. The tool has 3251 to 3253 for what an instance
// answers, and writes 3248 and 3250 out too.
internal static class TransferCommandErrorCodes
{
    public const string BackupNotSupported = "FCMS3240";
    public const string NoDatabaseFile = "FCMS3241";
    public const string InvalidArchive = "FCMS3242";
    public const string ArchiveNewer = "FCMS3243";
    public const string DatabaseExists = "FCMS3244";
    public const string DatabaseInUse = "FCMS3245";
    public const string MediaNotEmpty = "FCMS3246";
    public const string NoLocalMedia = "FCMS3247";
    public const string FileNotRead = "FCMS3248";
    public const string ImportNotClean = "FCMS3250";
}
