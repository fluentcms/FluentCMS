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
    /// Why the operation failed, or <see langword="null"/> when it did not: the problem the single call would have answered for
    /// the exception it threw. An entry the principal may not reach fails as one that does not exist.
    /// </summary>
    public BulkProblem? Problem { get; init; }

    /// <summary>What a reader of a published entry will miss, for <see cref="BulkOperation.Publish"/>.</summary>
    public IReadOnlyList<PublishWarning> Warnings { get; init; } = [];
}
