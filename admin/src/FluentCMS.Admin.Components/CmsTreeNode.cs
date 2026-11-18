namespace FluentCMS.Admin.Components;

/// <summary>A node of a <see cref="CmsTree"/>.</summary>
/// <param name="Id">The identifier the tree's selection binds to, unique in the tree.</param>
/// <param name="Text">The text shown for the node.</param>
/// <param name="Children">The child nodes, or <see langword="null"/> for a leaf.</param>
public sealed record CmsTreeNode(string Id, string Text, IReadOnlyList<CmsTreeNode>? Children = null)
{
    /// <summary>Gets whether the node shows its children when the tree first shows it; the user opens and closes it after that.</summary>
    public bool Expanded { get; init; }
}
