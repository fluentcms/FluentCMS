-- Test data of BaselineTests, not shipped: what the migrations of stream search ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
IF OBJECT_ID(N'[cms_search__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms_search__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms_search__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_search_entry] (
    [id] bigint NOT NULL IDENTITY,
    [document_id] uniqueidentifier NOT NULL,
    [locale] nvarchar(16) NOT NULL,
    [state] int NOT NULL,
    [type] nvarchar(64) NOT NULL,
    [site_id] uniqueidentifier NULL,
    [text] nvarchar(max) NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_search_entry] PRIMARY KEY ([id])
);

CREATE INDEX [ix_cms_search_entry_site] ON [cms_search_entry] ([site_id]);

CREATE INDEX [ix_cms_search_entry_type] ON [cms_search_entry] ([type], [locale], [state], [site_id]);

CREATE UNIQUE INDEX [ux_cms_search_entry_document] ON [cms_search_entry] ([document_id], [locale], [state]);

INSERT INTO [cms_search__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261113005245_InitialSearch', N'10.0.14');

COMMIT;
GO

