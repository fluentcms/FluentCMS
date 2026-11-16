namespace FluentCMS.Admin.Api;

/// <summary>An answer that is a list without paging, as <c>{ "items": [...] }</c>.</summary>
/// <typeparam name="T">The record of an item.</typeparam>
public sealed record ItemList<T>
{
    /// <summary>The items.</summary>
    public required IReadOnlyList<T> Items { get; init; }
}
