# FluentCMS.Data.Sqlite

The SQLite data provider of [FluentCMS](https://github.com/fluentcms/FluentCMS): its storage conventions, filters on JSON, a polling job runner, and the SQLite set of the system migrations, which it applies on start by default. The database runs in WAL mode.

```csharp
builder.Services.AddFluentCms(cms => cms.UseSqlite(builder.Configuration.GetConnectionString("FluentCms")));
```

Keep the database on a local disk, not a network share ([SQLite](https://github.com/fluentcms/FluentCMS/blob/main/docs/host-configuration.md#sqlite)), and back it up with `fluentcms backup` ([backups](https://github.com/fluentcms/FluentCMS/blob/main/docs/backups.md)).
