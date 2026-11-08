namespace FluentCMS.Content;

/// <summary>A target that <see cref="IReferenceResolver"/> resolved.</summary>
/// <param name="Value">What population returns in place of <c>{ "id", "type" }</c>, with <c>id</c> and <c>type</c> among its members.</param>
public sealed record ResolvedReference(ContentDocument Value)
{
    /// <summary>The URL a rich text renderer gives the target, or <see langword="null"/> when no public URL serves it.</summary>
    public string? Url { get; init; }

    /// <summary>The text a rich text renderer gives the target where its node has none, such as the alternative text of an image.</summary>
    public string? Text { get; init; }
}
