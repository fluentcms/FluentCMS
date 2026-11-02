namespace FluentCMS.Publishing;

/// <summary>How a value differs between two versions.</summary>
public enum VersionChangeKind
{
    /// <summary>The later version has a value where the earlier one had none.</summary>
    Added = 0,

    /// <summary>The earlier version had a value where the later one has none.</summary>
    Removed = 1,

    /// <summary>The value is different.</summary>
    Changed = 2,
}
