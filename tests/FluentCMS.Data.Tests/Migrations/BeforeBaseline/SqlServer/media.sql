-- Test data of BaselineTests, not shipped: what the migrations of stream media ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
IF OBJECT_ID(N'[cms_media__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms_media__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms_media__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_media_asset] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [folder_id] uniqueidentifier NULL,
    [file_name] nvarchar(255) NOT NULL,
    [storage_provider] nvarchar(64) NOT NULL,
    [storage_key] nvarchar(512) NOT NULL,
    [mime_type] nvarchar(128) NOT NULL,
    [size] bigint NOT NULL,
    [checksum] nvarchar(64) NOT NULL,
    [width] int NULL,
    [height] int NULL,
    [duration] float NULL,
    [title] nvarchar(256) NULL,
    [alt] nvarchar(max) NOT NULL,
    [caption] nvarchar(max) NOT NULL,
    [credit] nvarchar(256) NULL,
    [tags] nvarchar(max) NOT NULL,
    [focal_x] float NULL,
    [focal_y] float NULL,
    [is_private] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] uniqueidentifier NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    [updated_by] uniqueidentifier NOT NULL,
    [deleted_at] datetimeoffset NULL,
    CONSTRAINT [PK_cms_media_asset] PRIMARY KEY ([id])
);

CREATE TABLE [cms_media_folder] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [parent_id] uniqueidentifier NULL,
    [name] nvarchar(128) NOT NULL,
    [path] nvarchar(1024) NOT NULL,
    CONSTRAINT [PK_cms_media_folder] PRIMARY KEY ([id])
);

CREATE INDEX [ix_cms_media_asset_site_created_at] ON [cms_media_asset] ([site_id], [created_at]);

CREATE INDEX [ix_cms_media_asset_site_folder] ON [cms_media_asset] ([site_id], [folder_id]);

CREATE INDEX [ix_cms_media_folder_site_path] ON [cms_media_folder] ([site_id], [path]);

CREATE UNIQUE INDEX [ux_cms_media_folder_site_parent_name] ON [cms_media_folder] ([site_id], [parent_id], [name]) WHERE [parent_id] IS NOT NULL;

INSERT INTO [cms_media__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261108191519_InitialMedia', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_media_preset] (
    [site_id] uniqueidentifier NOT NULL,
    [name] nvarchar(64) NOT NULL,
    [params] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_cms_media_preset] PRIMARY KEY ([site_id], [name])
);

CREATE TABLE [cms_media_signing_key] (
    [id] int NOT NULL,
    [key] nvarchar(max) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_media_signing_key] PRIMARY KEY ([id])
);

CREATE TABLE [cms_media_variant] (
    [asset_id] uniqueidentifier NOT NULL,
    [params_hash] nvarchar(64) NOT NULL,
    [storage_key] nvarchar(512) NOT NULL,
    [size] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_media_variant] PRIMARY KEY ([asset_id], [params_hash])
);

INSERT INTO [cms_media__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261108191532_MediaTransforms', N'10.0.14');

COMMIT;
GO

