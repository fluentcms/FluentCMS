namespace FluentCMS.Hooks;

/// <summary>
/// Hooks into creating an entry, after it is written. Register it in the service collection; it runs inside the operation's transaction, in <see cref="HookOrderAttribute"/> order.
/// </summary>
/// <typeparam name="T">A content class, to see entries of that type, or <see cref="Content.ContentDocument"/>, to see entries of every type.</typeparam>
public interface IAfterCreateHook<T>
{
    /// <summary>Runs after the write, still inside the transaction, so a failure or a cancellation rolls the write back. <see cref="HookContext{T}.Entry"/> is a copy of what was written, so a change to it is discarded.</summary>
    /// <param name="context">The operation, the entry and the way to cancel.</param>
    /// <param name="cancellationToken">Cancels the hook.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask AfterCreateAsync(HookContext<T> context, CancellationToken cancellationToken);
}
