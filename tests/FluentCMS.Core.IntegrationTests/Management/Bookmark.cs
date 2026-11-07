using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Management;

// Fields with a storage default length and none declared, for StorageLengthTests.
[Collection("bookmark")]
public sealed class Bookmark : ContentItem
{
    [Slug]
    public string Slug { get; set; } = "";

    [Url]
    public string? Link { get; set; }
}
