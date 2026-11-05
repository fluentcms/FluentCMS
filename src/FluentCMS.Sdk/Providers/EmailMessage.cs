namespace FluentCMS.Providers;

/// <summary>An email to one recipient.</summary>
/// <param name="To">The address of the recipient.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="TextBody">The plain text body.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody)
{
    /// <summary>An HTML body to send alongside the text, or <see langword="null"/> for text only.</summary>
    public string? HtmlBody { get; init; }
}
