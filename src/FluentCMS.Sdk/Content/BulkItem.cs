namespace FluentCMS.Content;

/// <summary>One entry of a <see cref="BulkRequest"/> that names its entries by id.</summary>
/// <param name="Id">The id of the entry.</param>
public sealed record BulkItem(Guid Id)
{
    /// <summary>The JSON merge patch of an update. Required for <see cref="BulkOperation.Update"/> and refused otherwise.</summary>
    public ContentDocument? Patch { get; init; }

    /// <summary>
    /// The version the change was based on. Required for <see cref="BulkOperation.Update"/>, optional for <see cref="BulkOperation.Publish"/>,
    /// where it is the version that was reviewed, and refused otherwise.
    /// </summary>
    public int? ExpectedVersion { get; init; }
}
