using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Data.PostgreSql;

public sealed class PostgreSqlStorageConventions : StorageConventions
{
    // Byte order, which in UTF-8 is code point order, as SQLite and SQL Server's binary collation sort. The database's own
    // collation, usually a language's, would sort and compare by its rules instead.
    internal const string CodePointCollation = "C";

    public override bool ReordersJson => true;

    // Npgsql writes a DateTimeOffset to timestamptz only with offset 0.
    public override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();

    public override void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column) =>
        column.HasColumnType("jsonb");

    public override void ConfigureTextColumn(EntityTypeBuilder entity, PropertyBuilder column) =>
        column.UseCollation(CodePointCollation);

    public override void ConfigureJsonIndex(EntityTypeBuilder entity, string column) =>
        entity.HasIndex(column).HasMethod("gin").HasDatabaseName($"gin_{entity.Metadata.GetTableName()}_{column}");

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value);
}
