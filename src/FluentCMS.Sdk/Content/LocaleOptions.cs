namespace FluentCMS.Content;

/// <summary>Which locale of an entry an operation of <see cref="IContentService"/> acts on.</summary>
public sealed record LocaleOptions
{
    /// <summary>The locale, or <see langword="null"/> for that of the operation.</summary>
    public string? Locale { get; init; }
}
