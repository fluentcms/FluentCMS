namespace FluentCMS.Schema;

/// <summary>Renders a <see cref="RichText"/> value into an output format.</summary>
public interface IRichTextRenderer
{
    /// <summary>The formats this renderer supports, see <see cref="RichTextFormats"/>.</summary>
    IReadOnlyCollection<string> Formats { get; }

    /// <summary>Renders a rich text value.</summary>
    /// <param name="value">The value to render.</param>
    /// <param name="format">One of <see cref="Formats"/>, compared without regard to case.</param>
    /// <param name="options">How entry links and media embeds are resolved, or <see langword="null"/> to render placeholders.</param>
    /// <returns>The rendered document.</returns>
    /// <exception cref="ArgumentException">The format is not supported.</exception>
    /// <exception cref="FormatException">The value is not a valid rich text document.</exception>
    string Render(RichText value, string format, RichTextRenderOptions? options = null);
}
