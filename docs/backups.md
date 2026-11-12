# Backups

A backup of a FluentCMS instance is its database and its media files, taken together. This guide says what belongs to one, how to take it and put it back on each database (SQLite, PostgreSQL and SQL Server) and each media storage (the local disk, S3 and Azure Blob), and why an export package is no backup.

## What belongs to a backup

- **The database.** It holds everything the instance knows: content with its versions, the trash and the schedules, sites and locales, settings, roles, users, members, API keys, OAuth clients and tokens, the activity log, jobs, the schema history, and the tables of every plugin. It also holds the Data Protection key ring (`cms_data_protection_key`), so a restored instance reads the cookies and protected values it wrote, and, unless a signing certificate is configured, the keys that sign tokens. A database restored without its key ring signs users out and makes protected settings unreadable.
- **The media files.** The originals and variants the media plugin keeps in its storage. Assets in the database refer to them by storage and key, so the two belong to the same moment. Take the database first and the files second: a deleted asset keeps its file for `FluentCms:Media:DeletedRetention` (30 days), so files copied after the database cover every asset it names, and a file uploaded in between is only one more.
- **Not in either.** Configuration (`appsettings.json`, environment variables, the schema file of `FLUENTCMS_SCHEMA`), the signing certificate and the key ring certificate, and secrets such as `FluentCms:Media:SigningKey` live outside the instance. Keep them in source control or in a secret store, since a restore needs them too.

## An export package is not a backup

`fluentcms export` and `POST /api/management/v1/transfer/export` write a package of one site: its content, settings and, when asked, its roles and media files. It is for moving content between instances, such as from staging to production. It leaves out users, members, API keys, the activity log, versions, schedules, the trash and the key ring, so an instance rebuilt from packages has none of them. Use the backups below to recover an instance, and packages to move content.

## SQLite

A host on SQLite is backed up and restored with the `fluentcms` tool, in project mode: the tool builds the host and runs the command inside it, with its configuration and plugins.

```bash
fluentcms backup --output backups/fluentcms-2026-11-12.zip
fluentcms restore backups/fluentcms-2026-11-12.zip
```

`backup` takes the snapshot while the host runs. It goes through SQLite's online backup, which copies every page under one read transaction, so the copy is consistent and the host goes on writing in WAL mode meanwhile; copying `fluentcms.db` by hand is not, since recent commits are still in `fluentcms.db-wal`. Then it adds the files of the local media storage, under `FluentCms:Media:Local:Root`, leaving out the temporary files of an upload in progress. The archive is a zip with `backup.json` (the FluentCMS version, the provider, the schema hash, the applied migrations of every stream, the time and the number of media files), `database/fluentcms.db` and `media/`. When new uploads go to S3 or Azure Blob, the archive has the database and the files that are still on the local disk, and the command says that the files in the cloud storage are not in it: back those up as below.

`restore` puts a backup back into a host that is stopped. It refuses, before it changes anything:

- an archive that is no backup of `fluentcms backup` (`FCMS3242`);
- a backup of a newer FluentCMS than the host's (`FCMS3243`): restore into the same or a newer version, and never go back to an older one;
- a database that exists, unless `--force` is given (`FCMS3244`), which replaces it and loses what was written to it since the backup;
- a database that another process has open, such as a running host (`FCMS3245`): stop the host first. This is seen for a host in WAL mode, the default; a host that keeps the rollback journal holds no lock while idle, so make sure it is stopped;
- media files over files the host has, unless `--force` is given (`FCMS3246`), which writes the backup's files over them and leaves the others;
- media files in a host with no local media storage (`FCMS3247`).

It then moves the database it replaces aside, writes the database file, removes the journal files of the old one, deletes it, writes the media files into the host's `Local:Root`, and lists the migrations the host has that the backup had not. Apply them with `fluentcms migrate`, or start the host, which migrates on start on SQLite. When a media file cannot be written, the command says how many were and exits with `FCMS3307`: fix the cause and run it again with `--force`. Restoring into an empty folder gives the same users, keys, content, versions and media files as the instance had at the backup.

With the reference image, keep the media on the volume too with `FluentCms__Media__Local__Root=/data/media`. Back up a running container with `docker exec`, and restore into a stopped one with a container of its own on the same volume:

```bash
docker exec fluentcms dotnet /app/FluentCMS.Host.dll fluentcms backup --output /data/backups/fluentcms.zip

docker stop fluentcms
docker run --rm -v fluentcms-data:/data -e FluentCms__Media__Local__Root=/data/media \
  fluentcms fluentcms restore /data/backups/fluentcms.zip --force
docker start fluentcms
```

Copy the archive off the machine: a backup on the same disk as the database is lost with it.

| Code | Meaning |
| --- | --- |
| `FCMS3240` | The host runs on PostgreSQL or SQL Server, whose own tools back it up; see below. |
| `FCMS3241` | The SQLite database is in memory or named by a URI, or its file does not exist. |
| `FCMS3242` | The archive is no backup of `fluentcms backup`. |
| `FCMS3243` | The backup is of a newer FluentCMS than the host. |
| `FCMS3244` | The database exists and `--force` is not given. |
| `FCMS3245` | Another process has the database open. |
| `FCMS3246` | The media folder has files and `--force` is not given. |
| `FCMS3247` | The backup has media files and the host has no local media storage. |
| `FCMS3248` | The file given cannot be read. |

## PostgreSQL

`fluentcms backup` and `restore` refuse on PostgreSQL with `FCMS3240`: the server's own tools take a consistent backup of a running database and restore it, and a managed service adds point-in-time recovery.

```bash
pg_dump --format=custom --file=fluentcms.dump "host=db.example.com dbname=fluentcms user=backup"
pg_restore --clean --if-exists --no-owner --dbname="host=db.example.com dbname=fluentcms user=fluentcms" fluentcms.dump
```

`pg_dump` reads in one snapshot, so the instance may keep running. Restore into an empty database, or with `--clean`, with the instances stopped, then run `fluentcms migrate` when the instances are of a newer FluentCMS than the dump. For recovery to any moment rather than to the last dump, use continuous archiving of the WAL with a base backup, or the point-in-time restore of a managed service such as Azure Database for PostgreSQL or Amazon RDS.

## SQL Server

`fluentcms backup` and `restore` refuse on SQL Server with `FCMS3240`. Back the database up with `BACKUP DATABASE`, which is consistent while the instance runs:

```sql
BACKUP DATABASE fluentcms TO DISK = N'/var/opt/mssql/backup/fluentcms.bak' WITH COMPRESSION, CHECKSUM, INIT;
RESTORE DATABASE fluentcms FROM DISK = N'/var/opt/mssql/backup/fluentcms.bak' WITH REPLACE, CHECKSUM;
```

Restore with the instances stopped, then run `fluentcms migrate` when they are of a newer FluentCMS. The restored database keeps `READ_COMMITTED_SNAPSHOT`, which the system migrations turn on ([ADR 0005](decisions/0005-sql-server-read-committed-snapshot.md)); a database restored from elsewhere gets it from the next `fluentcms migrate`. Azure SQL Database takes automated backups and restores to a point in time or, with long-term retention, to a weekly or monthly backup, from the portal or with `az sql db restore`; `BACKUP DATABASE` to a disk is not available there.

## Media storage

- **Local disk.** On SQLite, `fluentcms backup` includes `FluentCms:Media:Local:Root`. On PostgreSQL and SQL Server, copy that folder after the database dump, with the tool of the volume (a snapshot of the disk, `rsync`, `robocopy`). Several instances share it on one volume, so back it up once. After a move to S3 or Azure Blob, the assets uploaded before the move are still served from it: keep backing it up until they are gone.
- **S3.** Turn on bucket versioning, so a file overwritten or deleted can be brought back, and replicate the bucket to another region or account, or copy it with `aws s3 sync s3://fluentcms-media s3://fluentcms-media-backup`. To restore, copy the objects back under the same keys.
- **Azure Blob.** Turn on soft delete and versioning for the container, and point-in-time restore for block blobs, which restores the container to a moment that matches a database restore. Copy it elsewhere with `azcopy sync`. To restore, copy the blobs back under the same names.

Whatever the storage, restore the database first and the files second, to the same storage and keys, and start the instances last.
