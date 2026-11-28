# Backups

A backup of a FluentCMS instance is its database and its media files, taken together. This guide says what belongs to one, how to take it and put it back on each database (SQLite, PostgreSQL and SQL Server) and each media storage (the local disk, S3 and Azure Blob), and why an export package is no backup.

## What belongs to a backup

- **The database.** It holds everything the instance knows: content with its versions, the trash and the schedules, sites and locales, settings, roles, users, members, API keys, OAuth clients and tokens, the activity log, jobs, the schema history, and the tables of every plugin. It also holds the Data Protection key ring (`cms_data_protection_key`), so a restored instance reads the cookies and protected values it wrote, and, unless a signing certificate is configured, the keys that sign tokens. A database restored without its key ring signs users out and makes protected settings unreadable.
- **The media files.** The originals and variants the media plugin keeps in its storage. Assets in the database refer to them by storage and key, so the two belong to the same moment. Take the database first and the files second: a deleted asset keeps its file for `FluentCms:Media:DeletedRetention` (30 days), so files copied after the database cover every asset it names, and a file uploaded in between is only one more.
- **Not in either.** Configuration (`appsettings.json`, environment variables, the schema file of `FLUENTCMS_SCHEMA`), the signing certificate and the key ring certificate, and secrets such as `FluentCms:Media:SigningKey` live outside the instance. Keep them in source control or in a secret store, since a restore needs them too.

## An export package is not a backup

`fluentcms export` and `POST /api/management/v1/transfer/export` write a package of one site: its content, settings and, when asked, its roles and media files. It is for moving content between instances, such as from staging to production. It leaves out users, members, API keys, the activity log, versions, schedules, the trash and the key ring, so an instance rebuilt from packages has none of them. Use the backups below to recover an instance, and packages to move content. An export reads its entries from one snapshot of the database, which it holds for `FluentCms:Transfer:ExportSnapshotTimeout` (1 hour) at most from the start, however steadily the package is read: an export of a large site over a slow link that takes longer is stopped, the download ends with a reset connection, and no package is left. `fluentcms export --url` then fails with `FCMS3252`, the instance stopped answering, and only the server's log says why (warning 9190, which names the option); `fluentcms export` run in the host fails with a cancellation that names the option. Raise the option for such an export, as `FluentCms__Transfer__ExportSnapshotTimeout=02:00:00`.

## SQLite

A host on SQLite is backed up and restored with the `fluentcms` tool, in project mode: the tool builds the host and runs the command inside it, with its configuration and plugins.

```bash
fluentcms backup --output backups/fluentcms-2026-11-12.zip
fluentcms restore backups/fluentcms-2026-11-12.zip
```

`backup` takes the snapshot while the host runs. It goes through SQLite's online backup, which copies every page under one read transaction, so the copy is consistent and the host goes on writing in WAL mode meanwhile; copying `fluentcms.db` by hand is not, since recent commits are still in `fluentcms.db-wal`. Then it adds the files of the local media storage, under `FluentCms:Media:Local:Root`, leaving out the temporary files of an upload in progress. The archive is a zip with `backup.json` (the FluentCMS version, the provider, the schema hash, the applied migrations of every stream, the time and the number of media files), `database/fluentcms.db` and `media/`. When new uploads go to S3 or Azure Blob, the archive has the database and the files that are still on the local disk, and the command says that the files in the cloud storage are not in it: back those up as below.

Choose an `--output` outside the local media folder. Before opening the database or creating files, `backup` refuses the media folder and its descendants, the database itself and its `-wal`, `-shm` and `-journal` files (`FCMS3254`). It also refuses ambiguous paths: a case-only difference is not enough, and a new archive beside a database with a non-ASCII name can be refused even if the names look different. Use a separate, clearly named backup directory in that case. Use a trusted directory whose links and mounts stay unchanged during the backup; the check does not identify a second bind mount or network share of the same storage. An existing archive at a safe destination is replaced on success.

The archive is a secret: it holds the password hashes, the key hashes, the tokens and the Data Protection key ring. On Linux and macOS `backup` writes it, and the database snapshot it takes first, readable and writable by its owner only (mode `600`); on Windows the file takes the permissions of its folder, so write it to a folder that only the operators can read. Keep it that way wherever it goes: copy it with a tool that keeps the mode (`scp -p`, `rsync -p`), keep it in a store that only the operators reach, encrypt it before it leaves the machine (for example with `age` or `gpg`), and never put it in source control.

`restore` puts a backup back into a host that is stopped. It refuses, before it changes anything:

- an archive that is no backup of `fluentcms backup` (`FCMS3242`);
- a backup of a newer FluentCMS than the host's (`FCMS3243`): restore into the same or a newer version, and never go back to an older one;
- a backup of another schema, or with a migration that the host does not have, unless `--allow-schema-mismatch` is given (`FCMS3249`). The command compares the schema hash and the applied migrations of every stream in `backup.json` with the host's and names what differs. A host would refuse to start on such a database when it lacks a table or column (`FCMS1301`), and content of a type the host does not have would stay in tables that nothing reads. With `--allow-schema-mismatch` the backup is restored anyway and, once the database is in place, what differs is printed as a warning. `--force` does not allow it: it only replaces a database and media files that exist. A migration that the host has and the backup had not is no difference: it is pending, which is what restoring into a newer host is for, so a restore into a host whose schema changed since the backup needs `--allow-schema-mismatch`, then `fluentcms migrate`;
- a database that exists, unless `--force` is given (`FCMS3244`), which replaces it and loses what was written to it since the backup;
- a database that another process has open, such as a running host (`FCMS3245`): stop the host first. This is seen for a host in WAL mode, the default. A host that keeps the rollback journal holds no lock while idle, so it is not seen, and on Linux it would go on writing to the database it had open, which the restore deletes; the text of `FCMS3244` says so too. Make sure every such host is stopped before you pass `--force`;
- media files over files the host has, unless `--force` is given (`FCMS3246`), which writes the backup's files over them and leaves the others;
- media files in a host with no local media storage (`FCMS3247`).

It then unpacks the database beside its file and runs `PRAGMA quick_check` on it, so that an entry that is not a SQLite database, or one cut off, is refused (`FCMS3242`) before the database it would replace is touched. It moves that database aside with its journal files (`-wal`, `-shm`, `-journal`), and moves them back when one of them or the new database file cannot be moved, so a failed move loses nothing, not even the commits that a host that was killed left in its WAL. Should they not go back either, the command (`FCMS3307`) names the files beside the database that hold them, `<database>.<id>.replaced` and its journal files, to rename by hand. Then it moves the new database file into place, deletes the old one and its journal files, writes the media files into the host's `Local:Root`, and lists the migrations the host has that the backup had not. Apply them with `fluentcms migrate`, or start the host, which migrates on start on SQLite. When a media file cannot be written, the command says how many were and exits with `FCMS3307`: fix the cause and run it again with `--force`. Restoring into an empty folder gives the same users, keys, content, versions and media files as the instance had at the backup.

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
| `FCMS3242` | The archive is no backup of `fluentcms backup`, or its database is not a whole SQLite database. |
| `FCMS3243` | The backup is of a newer FluentCMS than the host. |
| `FCMS3244` | The database exists and `--force` is not given. |
| `FCMS3245` | Another process has the database open. |
| `FCMS3246` | The media folder has files and `--force` is not given. |
| `FCMS3247` | The backup has media files and the host has no local media storage. |
| `FCMS3248` | The file given cannot be read. |
| `FCMS3249` | The backup is of another schema, or has a migration the host does not have, and `--allow-schema-mismatch` is not given; with it, a warning. |
| `FCMS3254` | The backup destination is unsafe: use a different path, outside the database, its journal files and the local media folder. |

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
