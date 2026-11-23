using System.Globalization;

namespace FluentCMS.Core.Content;

// A write that gave up waiting, at the lock timeout, for what another transaction held: the rows of the entry, or on SQLite
// the database. It rolled back whole and is not run again here, since the holder may run for as long as its hooks do, so
// the caller is told to try again (spec 4.5, "Lock waits as built").
internal sealed class EntryBusyException(TimeSpan lockTimeout, Exception inner) : Exception(
    string.Create(CultureInfo.InvariantCulture, $"Another write held what this write needed for longer than the lock timeout of {lockTimeout}, so it gave up and changed nothing; try it again in a moment."),
    inner)
{
    // The retry waits for the holder again, up to the lock timeout, so a longer pause would only delay it.
    public const int RetryAfterSeconds = 1;
}
