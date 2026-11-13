# FluentCMS.Data.SqlServer

The SQL Server data provider of [FluentCMS](https://github.com/fluentcms/FluentCMS): its storage conventions, filters on JSON with `OPENJSON`, job claiming with `READPAST`, and the SQL Server set of the system migrations, which turns `READ_COMMITTED_SNAPSHOT` on. SQL Server 2019 and 2025 are the tested versions.

```csharp
builder.Services.AddFluentCms(cms => cms.UseSqlServer(builder.Configuration.GetConnectionString("FluentCms")));
```

The provider does not migrate on start by default: apply the migrations with `fluentcms migrate` before the host starts ([system migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/system-migrations.md)).
