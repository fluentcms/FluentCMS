namespace FluentCMS.Hooks;

/// <summary>
/// Hooks into publishing an entry, after it is written. Register it in the service collection; it runs inside the operation's transaction, in <see cref="HookOrderAttribute"/> order.
/// </summary>
/// <typeparam name="T">A content class, to see entries of that type, or <see cref="Content.ContentDocument"/>, to see entries of every type.</typeparam>
public interface IAfterPublishHook<T>
{
    /// <summary>Runs after the write, still inside the transaction, so a failure or a cancellation rolls the write back.</summary>
    /// <param name="context">The operation, the entry and the way to cancel.</param>
    /// <param name="cancellationToken">Cancels the hook.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask AfterPublishAsync(HookContext<T> context, CancellationToken cancellationToken);
}
