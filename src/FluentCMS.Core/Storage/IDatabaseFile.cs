namespace FluentCMS.Core.Storage;

// The slot of `fluentcms backup` and `fluentcms restore` (spec 4.15), which a data provider fills when its database is one
// file on this machine. Only SQLite does; core has no reference to it. A server database is backed up with its own tools.
internal interface IDatabaseFile
{
    // The full path of the file, or null when the database is in memory.
    string? FilePath { get; }

    // Writes a consistent copy of the database to a file that is new or empty, while the host may be writing to it.
    void Snapshot(string destination);

    // The local originals named by a completed snapshot, not by the live database.
    IEnumerable<SnapshotOriginal> LocalOriginals(string snapshot);

    // Whether another connection, such as one of a running host, has the database open.
    bool IsInUse();

    // Why a snapshot is not a whole database that the provider reads, or null when it is one.
    string? Check(string snapshot);

    // Moves a snapshot, a file on the same volume, in place of the database, with nothing left beside it of the database it
    // replaces, which is put back when the move fails. Nothing may have the database open.
    void Restore(string snapshot);
}

internal sealed record SnapshotOriginal(string Key, long Size, string Checksum);
