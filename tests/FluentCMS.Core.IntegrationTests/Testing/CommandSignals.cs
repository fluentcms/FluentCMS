using System.Data.Common;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Testing;

// Signals each command a test picks, reads apart from writes, so that a test waits for what a background loop does rather
// than for a while.
internal sealed class CommandSignals(Func<DbCommand, bool> picks) : DbCommandInterceptor
{
    public Signals Reads { get; } = new();

    public Signals Writes { get; } = new();

    // The commands on the row of one lock in cms_lock: a waiter reads it while another holds the lock, a holder writes it.
    public static CommandSignals OfLock(string name) => new(command =>
        command.CommandText.Contains("\"cms_lock\"", StringComparison.Ordinal)
        && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, name)));

    public static CommandSignals OfTable(string table) => new(command => command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal));

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Seen(command);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Seen(command);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Seen(command);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Seen(command);
        return ValueTask.FromResult(result);
    }

    // SaveChanges writes through a reader too, so the text tells a read from a write.
    private void Seen(DbCommand command)
    {
        if (picks(command))
        {
            (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ? Reads : Writes).Add();
        }
    }

    internal sealed class Signals
    {
        private readonly Channel<bool> _seen = Channel.CreateUnbounded<bool>();

        public void Add() => _seen.Writer.TryWrite(true);

        public void Forget()
        {
            while (_seen.Reader.TryRead(out _))
            {
            }
        }

        // The next commands after what was seen and not yet waited for, or forgotten.
        public async Task NextAsync(int count, TimeSpan patience)
        {
            var token = TestContext.Current.CancellationToken;
            for (var seen = 0; seen < count; seen++)
            {
                await _seen.Reader.ReadAsync(token).AsTask().WaitAsync(patience, token);
            }
        }
    }
}
