using System.Text.Json;

namespace FluentCMS.Schema;

/// <summary>What an <see cref="IRichTextNodeRenderer"/> is given about the node it renders.</summary>
public interface IRichTextNodeContext
{
    /// <summary>The format being rendered, see <see cref="RichTextFormats"/>.</summary>
    string Format { get; }

    /// <summary>The node type, or the block type name for a typed inline block.</summary>
    string NodeType { get; }

    /// <summary>The node attributes, or the block data for a typed inline block.</summary>
    IReadOnlyDictionary<string, JsonElement> Attributes { get; }

    /// <summary>Whether the node sits inside inline content, such as a paragraph, rather than between blocks.</summary>
    bool IsInline { get; }

    /// <summary>Renders the content of the node in the current format.</summary>
    /// <returns>The rendered content, or an empty string for a node without content.</returns>
    string RenderChildren();
}
