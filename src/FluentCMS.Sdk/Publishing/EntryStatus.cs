namespace FluentCMS.Publishing;

/// <summary>The publishing state of an entry in one locale.</summary>
public enum EntryStatus
{
    /// <summary>Never published.</summary>
    Draft = 0,

    /// <summary>Published, and the draft equals the published version.</summary>
    Published = 1,

    /// <summary>Published, and the draft is newer than the published version.</summary>
    Modified = 2,

    /// <summary>Was published and has been withdrawn.</summary>
    Unpublished = 3,
}
