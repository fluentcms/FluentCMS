namespace FluentCMS.Admin.Content;

// The statuses of an entry in one locale that the form's controls depend on, as the Management API writes them.
internal static class EntryStatuses
{
    public const string Published = "published";

    // Published, with a newer draft.
    public const string Modified = "modified";
}
