namespace FluentCMS.Transfer;

/// <summary>What an import did with one item of a package.</summary>
public enum TransferOutcome
{
    /// <summary>The instance did not have it, and the import created it.</summary>
    Created = 1,

    /// <summary>The instance had an older copy, and the import replaced it.</summary>
    Updated = 2,

    /// <summary>The instance has the same copy, so nothing was written.</summary>
    Skipped = 3,

    /// <summary>The instance has a newer copy, which the import kept because it was not asked to overwrite.</summary>
    Conflict = 4,

    /// <summary>It could not be imported, and the rest of the package was.</summary>
    Error = 5,
}
