namespace FluentCMS.Core.ChangeSets;

public static class ChangeSetErrorCodes
{
    public const string OperationKindInvalid = "operation_kind_invalid";
    public const string ItemKindInvalid = "item_kind_invalid";
    public const string ItemMissing = "item_missing";
    public const string BaseVersionInvalid = "base_version_invalid";
    public const string ItemDuplicate = "item_duplicate";
    public const string SiteMissing = "site_missing";
    public const string RationaleMissing = "rationale_missing";
    public const string SourceBlank = "source_blank";
    public const string NotOpen = "change_set_not_open";
    public const string OperationsMissing = "operations_missing";
    public const string TransitionNotAllowed = "transition_not_allowed";
    public const string RejectionReasonMissing = "rejection_reason_missing";
}
