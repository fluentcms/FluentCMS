namespace FluentCMS.Content;

/// <summary>What a <see cref="BulkRequest"/> does to each of its entries.</summary>
public enum BulkOperation
{
    /// <summary>Creates a draft for each of <see cref="BulkRequest.Entries"/>, as <see cref="IContentService.CreateAsync"/>.</summary>
    Create = 0,

    /// <summary>Changes the draft of each entry with a JSON merge patch, as <see cref="IContentService.PatchAsync"/>.</summary>
    Update = 1,

    /// <summary>Publishes each entry, as <see cref="IContentService.PublishAsync"/>.</summary>
    Publish = 2,

    /// <summary>Withdraws the published state of each entry, as <see cref="IContentService.UnpublishAsync"/>.</summary>
    Unpublish = 3,

    /// <summary>Moves each entry to the trash, as <see cref="IContentService.DeleteAsync"/>.</summary>
    Delete = 4,
}
