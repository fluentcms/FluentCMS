namespace FluentCMS.Admin.Api;

/// <summary>What the caller may do with the entries of one type, as <see cref="MyAccessInfo.Types"/> lists it.</summary>
public sealed record TypeAccessInfo
{
    /// <summary>Whether the caller may read drafts, and not only published entries.</summary>
    public required bool Drafts { get; init; }

    /// <summary>Whether the caller may create entries.</summary>
    public required bool Create { get; init; }

    /// <summary>Whether the caller may change entries.</summary>
    public required bool Update { get; init; }

    /// <summary>Whether the caller may delete entries.</summary>
    public required bool Delete { get; init; }

    /// <summary>Whether the caller may publish entries.</summary>
    public required bool Publish { get; init; }

    /// <summary>Whether the caller may unpublish entries.</summary>
    public required bool Unpublish { get; init; }

    /// <summary>The fields the caller may set when it creates an entry.</summary>
    public required IReadOnlyList<string> CreatableFields { get; init; }

    /// <summary>The fields the caller may change.</summary>
    public required IReadOnlyList<string> UpdatableFields { get; init; }

    /// <summary>The operations the caller may do only on some entries, such as its own, which the server checks entry by entry.</summary>
    public required IReadOnlyList<string> Conditional { get; init; }
}
