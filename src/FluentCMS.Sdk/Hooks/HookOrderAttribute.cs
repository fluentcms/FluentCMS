namespace FluentCMS.Hooks;

/// <summary>
/// Sets where a hook runs among the hooks of the same operation: the lower the order, the earlier. A hook without
/// the attribute has order 0. Hooks of equal order run in plugin dependency order.
/// </summary>
/// <param name="order">The order.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HookOrderAttribute(int order) : Attribute
{
    /// <summary>The order.</summary>
    public int Order { get; } = order;
}
