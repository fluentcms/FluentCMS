namespace FluentCMS.Content;

/// <summary>Which locale of an entry a write of <see cref="IContentService"/> acts on, and the version of the draft it is based on.</summary>
public sealed record LocaleWriteOptions
{
    /// <summary>The locale, or <see langword="null"/> for that of the operation.</summary>
    public string? Locale { get; init; }

    /// <summary>The version of the draft the write is based on, or <see langword="null"/> for whatever the draft holds.</summary>
    public int? ExpectedVersion { get; init; }
}
