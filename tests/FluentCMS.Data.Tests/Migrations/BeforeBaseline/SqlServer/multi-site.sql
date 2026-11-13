-- Test data of BaselineTests, not shipped: what the migrations of stream multi-site ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
IF OBJECT_ID(N'[cms_multi_site__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms_multi_site__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms_multi_site__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_multi_site_host] (
    [hostname] nvarchar(253) NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [is_primary] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_multi_site_host] PRIMARY KEY ([hostname])
);

CREATE INDEX [ix_cms_multi_site_host_site] ON [cms_multi_site_host] ([site_id]);

INSERT INTO [cms_multi_site__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261111042240_InitialMultiSite', N'10.0.14');

COMMIT;
GO

