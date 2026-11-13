# FluentCMS.Data.PostgreSql

The PostgreSQL data provider of [FluentCMS](https://github.com/fluentcms/FluentCMS): its storage conventions, filters on JSON paths, job claiming with `SKIP LOCKED`, and the PostgreSQL set of the system migrations. PostgreSQL 15 is the tested version.

```csharp
builder.Services.AddFluentCms(cms => cms.UsePostgreSql(builder.Configuration.GetConnectionString("FluentCms")));
```

The provider does not migrate on start by default: apply the migrations with `fluentcms migrate` before the host starts ([system migrations](https://github.com/fluentcms/FluentCMS/blob/main/docs/system-migrations.md)).
