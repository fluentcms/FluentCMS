namespace FluentCMS.Core.Transfer;

// Export packages and their import (spec 4.14), from 3200 up to 3239.
internal static class TransferErrorCodes
{
    // The request.
    public const string InvalidPackage = "FCMS3200";
    public const string UnsupportedFormat = "FCMS3201";
    public const string SchemaIncompatible = "FCMS3202";
    public const string LocaleNotInSite = "FCMS3203";
    public const string PackageTooLarge = "FCMS3204";
    public const string ExportRunning = "FCMS3205";
    public const string InvalidOption = "FCMS3206";
    public const string ImportRunning = "FCMS3207";

    // One item of a report.
    public const string IdTaken = "FCMS3210";
    public const string InTrash = "FCMS3211";
    public const string InvalidItem = "FCMS3212";
    public const string UnknownArea = "FCMS3213";
    public const string NoContributor = "FCMS3214";
    public const string ItemFailed = "FCMS3215";

    // The host.
    public const string InvalidContributor = "FCMS3230";
}
