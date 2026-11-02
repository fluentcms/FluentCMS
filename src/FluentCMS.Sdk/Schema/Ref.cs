namespace FluentCMS.Schema;

/// <summary>A reference to one entry of content type <typeparamref name="T"/>. Equal when the ids are equal.</summary>
/// <typeparam name="T">The content type of the referenced entry.</typeparam>
/// <param name="Id">The id of the referenced entry.</param>
public readonly record struct Ref<T>(Guid Id)
{
    /// <summary>Whether the reference points to no entry.</summary>
    public bool IsEmpty => Id == Guid.Empty;

    /// <inheritdoc/>
    public override string ToString() => Id.ToString();
}
