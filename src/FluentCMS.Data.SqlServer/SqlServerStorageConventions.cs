using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Data.SqlServer;

// JSON is the native json type on SQL Server 2025 and checked nvarchar(max) text below it.
public sealed class SqlServerStorageConventions(bool nativeJson = false) : StorageConventions
{
    public override string CacheKey => $"{base.CacheKey}|{nativeJson}";

    public override void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column)
    {
        if (nativeJson)
        {
            column.HasColumnType("json");
            return;
        }

        column.HasColumnType("nvarchar(max)");

        var name = column.Metadata.GetColumnName();
        entity.ToTable(table => table.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{name}_json", $"ISJSON([{name}]) = 1"));
    }
}
