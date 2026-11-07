using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Data.SqlServer;

// JSON is the native json type on SQL Server 2025 and checked nvarchar(max) text below it.
public sealed class SqlServerStorageConventions(bool nativeJson = false) : StorageConventions
{
    // The other providers compare text case-sensitively, and a database's default collation usually does not.
    internal const string BinaryCollation = "Latin1_General_100_BIN2";

    public override string CacheKey => $"{base.CacheKey}|{nativeJson}";

    // datetimeoffset keeps the offset it is given, so a date would read back in the writer's offset.
    public override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();

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

    public override void ConfigureTextColumn(EntityTypeBuilder entity, PropertyBuilder column) =>
        column.UseCollation(BinaryCollation);

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value);
}
