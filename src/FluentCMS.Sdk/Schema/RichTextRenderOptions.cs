namespace FluentCMS.Schema;

/// <summary>Per call settings for <see cref="IRichTextRenderer.Render"/>.</summary>
public sealed class RichTextRenderOptions
{
    /// <summary>
    /// Resolves the URL of an entry link from the content type name and the entry id. Return
    /// <see langword="null"/> when the entry cannot be resolved; the link is then rendered as a
    /// placeholder that keeps the type and the id.
    /// </summary>
    public Func<string, Guid, string?>? EntryUrlResolver { get; init; }

    /// <summary>
    /// Resolves the URL of a media embed from the media id. Return <see langword="null"/> when the
    /// asset cannot be resolved; the embed is then rendered as a placeholder that keeps the id.
    /// </summary>
    public Func<Guid, string?>? MediaUrlResolver { get; init; }

    /// <summary>
    /// Resolves the alternative text of a media embed whose node has none, from the media id. Return <see langword="null"/>
    /// when there is none; the embed then has an empty one.
    /// </summary>
    public Func<Guid, string?>? MediaAltResolver { get; init; }
}
