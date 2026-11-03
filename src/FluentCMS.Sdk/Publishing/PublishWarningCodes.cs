namespace FluentCMS.Publishing;

/// <summary>The codes of what publishing reports about references, as warnings or, under <see cref="UnpublishedReferencePolicy.Block"/>, as validation errors.</summary>
public static class PublishWarningCodes
{
    /// <summary>The target exists and the principal may read it, but it has no published row in the locale or its fallbacks.</summary>
    public const string UnpublishedReference = "FCMS2403";

    /// <summary>The target cannot be reached: it does not exist, is in the trash or in another site, or the principal may not read it. These are not told apart.</summary>
    public const string UnavailableReference = "FCMS2404";
}
