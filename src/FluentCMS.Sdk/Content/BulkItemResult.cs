using FluentCMS.Publishing;

namespace FluentCMS.Content;

/// <summary>What became of one entry of a <see cref="BulkRequest"/>.</summary>
/// <param name="Index">The position of the entry in the request, or in the selection of a filter.</param>
/// <param name="Id">The id of the entry, or <see langword="null"/> for an entry that was not created.</param>
/// <param name="Status">What became of it.</param>
public sealed record BulkItemResult(int Index, Guid? Id, BulkItemStatus Status)
{
    /// <summary>The draft version after the operation, or <see langword="null"/> when it did not run or moved the entry to the trash.</summary>
    public int? Version { get; init; }

    /// <summary>
    /// Why the operation failed: the exception the single call would have thrown, such as <see cref="EntryNotFoundException"/>,
    /// <see cref="ContentAccessDeniedException"/>, <see cref="VersionConflictException"/>, <see cref="ValidationException"/> or
    /// <see cref="Hooks.HookRejectedException"/>. An entry the principal may not reach fails as one that does not exist. Any other
    /// failure is an <see cref="InvalidOperationException"/> that says nothing of its cause, which the host logs.
    /// </summary>
    public Exception? Problem { get; init; }

    /// <summary>What a reader of a published entry will miss, for <see cref="BulkOperation.Publish"/>.</summary>
    public IReadOnlyList<PublishWarning> Warnings { get; init; } = [];
}
