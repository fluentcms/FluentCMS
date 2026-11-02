namespace FluentCMS.Content;

/// <summary>Which page of a <see cref="ContentQuery"/> to read: by page number, by offset or, with <see cref="ContentQuery.Cursor"/>, by neither.</summary>
public sealed record PageRequest
{
    /// <summary>The page number, starting at one. It cannot be combined with <see cref="Offset"/>.</summary>
    public int? Number { get; init; }

    /// <summary>The number of entries to skip. It cannot be combined with <see cref="Number"/>.</summary>
    public int? Offset { get; init; }

    /// <summary>The page size, or <see langword="null"/> for the default.</summary>
    public int? Size { get; init; }

    /// <summary>Whether to count every matching entry. A page read by cursor is never counted.</summary>
    public bool WithTotal { get; init; } = true;
}
