namespace FluentCMS.Core.ChangeSets;

public static class ChangeSetErrorCodes
{
    public const string OperationKindInvalid = "operation_kind_invalid";
    public const string ItemKindInvalid = "item_kind_invalid";
    public const string ItemMissing = "item_missing";
    public const string BaseVersionInvalid = "base_version_invalid";
    public const string ProposedVersionInvalid = "proposed_version_invalid";
    public const string ItemDuplicate = "item_duplicate";
    public const string SiteMissing = "site_missing";
    public const string RationaleMissing = "rationale_missing";
    public const string SourceBlank = "source_blank";
    public const string SessionIdInvalid = "session_id_invalid";
    public const string NotOpen = "change_set_not_open";
    public const string OperationsMissing = "operations_missing";
    public const string TransitionNotAllowed = "transition_not_allowed";
    public const string RejectionReasonMissing = "rejection_reason_missing";
    public const string ChangeSetNotFound = "change_set_not_found";
    public const string ItemNotFound = "item_not_found";
    public const string VersionNotFound = "version_not_found";
    public const string ItemKindUnavailable = "item_kind_unavailable";
    public const string SessionHasOpenSet = "session_has_open_set";
    public const string Conflict = "change_set_conflict";
    public const string BaseVersionStale = "base_version_stale";
    public const string RevertFixed = "revert_set_fixed";
    public const string ItemKindMismatch = "item_kind_mismatch";
}
