namespace FluentCMS.Content;

/// <summary>What became of one entry of a <see cref="BulkRequest"/>.</summary>
public enum BulkItemStatus
{
    /// <summary>The operation ran and committed.</summary>
    Succeeded = 0,

    /// <summary>The operation was refused or failed, and left nothing behind. <see cref="BulkItemResult.Problem"/> says why.</summary>
    Failed = 1,

    /// <summary>The id was named earlier in the request, and the operation ran there.</summary>
    Duplicate = 2,

    /// <summary>The request was cancelled before the operation ran, or while it ran and before it committed.</summary>
    NotRun = 3,
}
