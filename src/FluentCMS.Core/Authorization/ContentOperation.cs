namespace FluentCMS.Core.Authorization;

public enum ContentOperation
{
    Create = 1,
    Read = 2,
    Update = 3,
    Delete = 4,
    Publish = 5,
    Unpublish = 6,
    Restore = 7,
    Purge = 8,
}
