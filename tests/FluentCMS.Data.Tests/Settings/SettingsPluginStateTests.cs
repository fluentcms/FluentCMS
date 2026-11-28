using System.Data.Common;
using FluentCMS.Core.Events;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Data.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Settings.SettingsDatabase;

namespace FluentCMS.Data.Tests.Settings;

public sealed class SettingsPluginStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AColdPluginStateReadNeedsNoSecondConnectionDuringTheSettingsTransaction(bool reset)
    {
        var connections = new StateReads();
        using var database = new SettingsDatabase(cms =>
        {
            cms.AddPlugin<StoreAlphaPlugin>();
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(connections));
        });
        database.AddRow(SiteA.Value, "media", "{}");
        database.Inner.Database.Services.GetRequiredService<PluginState>().Invalidate();

        await database.ServiceAsync(async (service, descriptor) =>
        {
            if (reset)
            {
                await service.ResetAsync(descriptor, SiteA, 1, Token);
            }
            else
            {
                await service.WriteAsync(descriptor, SiteA, """{"provider":"new"}""", 1, Token);
            }

            return true;
        });

        connections.Reads.ShouldBe(1);
        database.Inner.Rows().ShouldHaveSingleItem();
        database.Inner.Read(context => context.Set<Job>().Count(job => job.Type == OutboxDrainJob.Type)).ShouldBe(1);
    }

    private sealed class StateReads : DbCommandInterceptor, IDbTransactionInterceptor
    {
        private DbConnection? _leased;

        public int Reads { get; private set; }

        public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            _leased = connection;
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            _leased = null;
            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("cms_plugin_site", StringComparison.Ordinal))
            {
                Reads++;
                if (_leased is not null && _leased != command.Connection)
                {
                    throw new InvalidOperationException("Plugin state needed a second connection during the settings transaction.");
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
