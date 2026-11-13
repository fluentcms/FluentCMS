-- Test data of BaselineTests, not shipped: what the migrations of stream ai ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
IF OBJECT_ID(N'[cms_ai__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms_ai__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms_ai__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_ai_usage] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [principal_kind] nvarchar(32) NOT NULL,
    [principal_id] uniqueidentifier NOT NULL,
    [action] nvarchar(32) NOT NULL,
    [type] nvarchar(64) NOT NULL,
    [field] nvarchar(64) NOT NULL,
    [provider] nvarchar(64) NOT NULL,
    [model] nvarchar(128) NOT NULL,
    [prompt_tokens] bigint NOT NULL,
    [completion_tokens] bigint NOT NULL,
    [duration_ms] bigint NOT NULL,
    [succeeded] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_ai_usage] PRIMARY KEY ([id])
);

CREATE INDEX [ix_cms_ai_usage_created_at] ON [cms_ai_usage] ([created_at]);

CREATE INDEX [ix_cms_ai_usage_site_created_at] ON [cms_ai_usage] ([site_id], [created_at]);

INSERT INTO [cms_ai__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261111053857_InitialAi', N'10.0.14');

COMMIT;
GO

