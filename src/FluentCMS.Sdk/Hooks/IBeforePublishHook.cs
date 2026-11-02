namespace FluentCMS.Hooks;

/// <summary>
/// Hooks into publishing an entry, before it is written. Register it in the service collection; it runs inside the operation's transaction, in <see cref="HookOrderAttribute"/> order.
/// </summary>
/// <typeparam name="T">A content class, to see entries of that type, or <see cref="Content.ContentDocument"/>, to see entries of every type.</typeparam>
public interface IBeforePublishHook<T>
{
    /// <summary>Runs before the write. The hook may change <see cref="HookContext{T}.Entry"/>, and the change is what gets stored, or call <see cref="HookContext{T}.Cancel"/> to reject the operation and roll the transaction back.</summary>
    /// <param name="context">The operation, the entry and the way to cancel.</param>
    /// <param name="cancellationToken">Cancels the hook.</param>
    /// <returns>A task that completes when the hook is done.</returns>
    ValueTask BeforePublishAsync(HookContext<T> context, CancellationToken cancellationToken);
}
