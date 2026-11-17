using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Actions;

/// <summary>
/// Shows the progress of a bulk operation: a progress bar, the result of each entry as it comes, and a way to cancel. The
/// admin starts one for a <see cref="BulkActionBase"/>; a page of a half starts its own for an operation over many items.
/// </summary>
public interface IBulkProgressService
{
    /// <summary>Starts showing the progress of an operation.</summary>
    /// <param name="title">The name of the operation, from the resources of the caller.</param>
    /// <param name="total">The number of items the operation works on.</param>
    /// <returns>The progress, which the caller reports to and disposes when the operation has ended.</returns>
    IBulkProgress Start(string title, int total);
}

/// <summary>The progress of one bulk operation. Disposing it ends the operation and leaves its results shown.</summary>
public interface IBulkProgress : IDisposable
{
    /// <summary>Gets a token that is cancelled when the user cancels; the operation stops after the items it is working on.</summary>
    CancellationToken Cancelled { get; }

    /// <summary>Reports the result of one item.</summary>
    /// <param name="result">The result.</param>
    void Report(BulkItemResult result);
}

/// <summary>The result of one item of a bulk operation.</summary>
public sealed class BulkItemResult
{
    /// <summary>Gets the id of the item.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the status of the item after the operation, such as <c>published</c>, or <see langword="null"/> when it has none to show.</summary>
    public string? Status { get; init; }

    /// <summary>Gets the warnings the operation gave for the item.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Gets the problem of an item that failed, or <see langword="null"/> for one that succeeded.</summary>
    public ApiProblem? Problem { get; init; }
}
