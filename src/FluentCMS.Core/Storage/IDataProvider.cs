using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Core.Storage;

// Everything a data provider package gives core. Its UseXxx method passes one to FluentCmsBuilder.UseDataProvider.
public interface IDataProvider
{
    string Name { get; }

    // The default of FluentCms:Database:MigrateOnStartup.
    bool MigrateOnStartup { get; }

    // Whether FluentCms:Database:SchemaSync may be Auto in Development.
    bool SupportsSchemaSync { get; }

    IStorageConventions Conventions { get; }

    IJsonSqlDialect JsonDialect { get; }

    IJobClaimDialect JobClaimDialect { get; }

    IDatabaseSchemaReader SchemaReader { get; }

    // Whether the exception, or the database error that a DbUpdateException wraps, is a write that broke a unique key:
    // what a writer gets that lost a race to the row its checks found free (spec 4.4).
    bool IsUniqueViolation(Exception exception);

    // Whether the database ended the transaction as the victim of a deadlock, which it asks to be run again.
    bool IsDeadlock(Exception exception);

    // Whether a command gave up waiting for a database that has one writer at a time, while another connection's transaction
    // held it: a write, or a read in a journal mode where a commit keeps readers out. The command changed nothing. On SQLite
    // that is every error whose primary code is SQLITE_BUSY or SQLITE_LOCKED, extended codes such as BUSY_SNAPSHOT and
    // LOCKED_SHAREDCACHE included. False where writers do not wait for the whole database.
    bool IsBusy(Exception exception);

    // Whether the database has one writer at a time, which a write transaction keeps from its beginning to its end, so that
    // every write waits for every other connection's write transaction, whatever rows they touch. True on SQLite, whose
    // timings of that wait the default lock keeps in core (DatabaseDistributedLock).
    bool HasOneWriter { get; }

    // A condition of a LINQ query: the text holds the fragment, as it is (LikePattern), without regard to case as the database
    // compares text. A match on a part of the text uses no index.
    Expression<Func<string?, bool>> ContainsIgnoringCase(string fragment);

    // Points the options of CmsDbContext at the database, with the host's content migrations and their history table.
    void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly);

    // The same, for SystemDbContext: adds the assembly that holds the system migrations and their history table.
    void ConfigureSystemMigrations(DbContextOptionsBuilder options);

    // The same, for the context of a plugin's stream, with its assembly and its history table cms_{plugin}__migrations, and
    // UsePluginMigrationSet with the name of the provider's set in that assembly.
    void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable);

    // Holds the database while one process applies its streams, for a provider whose own migrations lock lets two processes
    // apply the same migration; disposing it lets the next go. Null where EF's lock is enough.
    Task<IAsyncDisposable?> LockMigrationsAsync(CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable?>(null);

    // The time written in the row of __EFMigrationsLock while one holds EF's migrations lock, as the database keeps it, for a
    // provider whose lock is that row, which a process that stops while it migrates leaves behind. Null when no row holds
    // it, and always where the lock ends with the session or transaction that took it.
    Task<string?> ReadMigrationsLockAsync(DbContext context, CancellationToken cancellationToken);

    // Begins a transaction on the context for a long read, such as an export, whose reads all see the database as it was at
    // the first of them, and that keeps no writer waiting. Null where the database gives none; each read then sees what was
    // committed when it ran.
    Task<IDbContextTransaction?> BeginSnapshotAsync(DbContext context, CancellationToken cancellationToken) => Task.FromResult<IDbContextTransaction?>(null);

    // Locks the row of a site in cms_site to the end of the transaction of the context, so that a write that adds an entry to
    // the site and the deletion of the site wait for each other: shared for the writes, which do not wait for one another,
    // and exclusive for the deletion. False when there is no such site. The default reads the row without a lock, which is
    // enough where the database has one writer.
    Task<bool> LockSiteAsync(DbContext context, Guid site, bool exclusive, CancellationToken cancellationToken) =>
        context.Set<Entities.Site>().AnyAsync(row => row.Id == site, cancellationToken);
}
