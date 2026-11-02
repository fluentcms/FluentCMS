namespace FluentCMS.Media;

/// <summary>A reference to one media asset. Equal when the ids are equal.</summary>
/// <param name="Id">The id of the media asset.</param>
public readonly record struct MediaRef(Guid Id)
{
    /// <summary>Whether the reference points to no asset.</summary>
    public bool IsEmpty => Id == Guid.Empty;

    /// <inheritdoc/>
    public override string ToString() => Id.ToString();
}
