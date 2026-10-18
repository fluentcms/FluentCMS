using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FluentCMS.Infrastructure.Persistence;

internal static class DatabaseErrors
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    // A concurrent writer passed the same check first and committed; the caller answers with the result its own check would give.
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException switch
        {
            PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
            SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraint
                && sqlite.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey,
            _ => false,
        };
    }
}
