namespace FluentCMS.Schema;

/// <summary>
/// Renders one node type, which lets a plugin add typed inline blocks. A typed inline block is
/// matched by its block type name, any other node by its node type, so a renderer registered for
/// a built in node type replaces the built in output.
/// </summary>
public interface IRichTextNodeRenderer
{
    /// <summary>The node type, or the block type name of a typed inline block, this renderer handles.</summary>
    string NodeType { get; }

    /// <summary>Renders the node.</summary>
    /// <param name="context">The node and the format.</param>
    /// <returns>
    /// The output, inserted as is, so a renderer for HTML encodes its own text. Return
    /// <see langword="null"/> to use the built in output, for example for a format it does not support.
    /// </returns>
    string? Render(IRichTextNodeContext context);
}
