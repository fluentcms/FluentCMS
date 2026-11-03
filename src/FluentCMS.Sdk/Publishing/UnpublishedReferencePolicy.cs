namespace FluentCMS.Publishing;

/// <summary>What publishing an entry does about what it refers to that readers cannot reach.</summary>
public enum UnpublishedReferencePolicy
{
    /// <summary>The entry is published, and the response says so in its warnings.</summary>
    Warn = 0,

    /// <summary>The entry is not published: a validation error.</summary>
    Block = 1,
}
