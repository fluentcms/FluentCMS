namespace FluentCMS.Providers;

/// <summary>
/// The provider slots of the host: services such as <see cref="IEmailSender"/>, <see cref="IDistributedLock"/> and
/// <c>IDistributedCache</c> that core or a plugin fills with a default and that the host or a plugin replaces. Core
/// implements it; host code and plugins only call it.
/// </summary>
public interface IProviderSlots
{
    /// <summary>
    /// Replaces the implementation of a slot with a singleton of <typeparamref name="TImplementation"/>, whatever the order
    /// in which the host, the plugins and core register their services. The host's choice wins over a plugin's, and the
    /// last call for a slot wins over the earlier ones of the same caller; two plugins that replace one slot stop the host
    /// unless the host chooses.
    /// </summary>
    /// <typeparam name="TSlot">The slot, such as <see cref="IEmailSender"/>.</typeparam>
    /// <typeparam name="TImplementation">The class that fills it.</typeparam>
    /// <returns>The same slots, to chain calls.</returns>
    IProviderSlots Use<TSlot, TImplementation>()
        where TSlot : class
        where TImplementation : class, TSlot;
}
