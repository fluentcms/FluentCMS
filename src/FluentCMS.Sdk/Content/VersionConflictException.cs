namespace FluentCMS.Content;

/// <summary>Thrown when a write was based on a version that is no longer the stored one. The HTTP layer maps it to a 412 with the current entry in the problem details.</summary>
public sealed class VersionConflictException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="expectedVersion">The version the write was based on.</param>
    /// <param name="currentVersion">The stored version.</param>
    /// <param name="current">The stored entry as the principal may read it, if it may.</param>
    public VersionConflictException(int expectedVersion, int currentVersion, ContentDocument? current)
        : base($"The write was based on version {expectedVersion}, but the entry is at version {currentVersion}.")
    {
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
        Current = current;
    }

    /// <summary>The version the write was based on.</summary>
    public int ExpectedVersion { get; }

    /// <summary>The stored version.</summary>
    public int CurrentVersion { get; }

    /// <summary>The stored entry without what the principal may not read, or <see langword="null"/> when it may not read the entry.</summary>
    public ContentDocument? Current { get; }
}
