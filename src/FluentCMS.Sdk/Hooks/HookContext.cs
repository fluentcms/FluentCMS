using FluentCMS.Content;

namespace FluentCMS.Hooks;

/// <summary>What a hook knows about the operation, and the way to cancel it.</summary>
/// <typeparam name="T">The type of the entry: a content class or <see cref="ContentDocument"/>.</typeparam>
public sealed class HookContext<T>
{
    /// <summary>Creates a context, for the hook pipeline or for unit tests of a hook.</summary>
    /// <param name="operation">The operation the hook runs in.</param>
    /// <param name="entry">The entry the operation works on.</param>
    /// <param name="previous">The stored state before the operation, or <see langword="null"/> when there is none.</param>
    /// <param name="changedPaths">The paths of the fields that differ between <paramref name="previous"/> and <paramref name="entry"/>.</param>
    public HookContext(OperationContext operation, T entry, T? previous, IReadOnlyList<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(changedPaths);

        Operation = operation;
        Entry = entry;
        Previous = previous;
        ChangedPaths = changedPaths;
    }

    /// <summary>Who performs the operation, from where, on which site and in which locale.</summary>
    public OperationContext Operation { get; }

    /// <summary>The entry the operation works on. A before hook may change it, and the change is what gets stored.</summary>
    public T Entry { get; }

    /// <summary>The stored state before the operation; <see langword="null"/> when an entry is created.</summary>
    public T? Previous { get; }

    /// <summary>The paths of the fields that differ between <see cref="Previous"/> and <see cref="Entry"/>, such as <c>title</c>, <c>seo.metaTitle</c> or <c>sections[2].heading</c>. A list whose length changed is reported as the list.</summary>
    public IReadOnlyList<string> ChangedPaths { get; }

    /// <summary>Whether <see cref="Cancel"/> was called.</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>The code given to the first call of <see cref="Cancel"/>, or <see langword="null"/> when the operation is not cancelled.</summary>
    public string? CancelCode { get; private set; }

    /// <summary>The message given to the first call of <see cref="Cancel"/>, or <see langword="null"/> when the operation is not cancelled.</summary>
    public string? CancelMessage { get; private set; }

    /// <summary>
    /// Rejects the operation. The pipeline stops after the hook returns, skips the hooks that follow, throws a
    /// <see cref="HookRejectedException"/> and the transaction rolls back. Only the first call counts.
    /// </summary>
    /// <param name="code">A stable code; plugins should use their own prefix.</param>
    /// <param name="message">A message for people.</param>
    public void Cancel(string code, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);

        if (IsCancelled)
        {
            return;
        }

        IsCancelled = true;
        CancelCode = code;
        CancelMessage = message;
    }
}
