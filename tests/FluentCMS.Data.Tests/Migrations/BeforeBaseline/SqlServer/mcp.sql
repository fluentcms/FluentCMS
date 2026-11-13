-- Test data of BaselineTests, not shipped: what the migrations of stream mcp ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
IF OBJECT_ID(N'[cms_mcp__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms_mcp__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms_mcp__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_mcp_instruction] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [type] nvarchar(64) NULL,
    [text] nvarchar(max) NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    [updated_by] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_mcp_instruction] PRIMARY KEY ([id])
);

CREATE TABLE [cms_mcp_prompt] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [name] nvarchar(64) NOT NULL,
    [title] nvarchar(100) NULL,
    [description] nvarchar(500) NULL,
    [arguments] nvarchar(max) NOT NULL,
    [template] nvarchar(max) NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    [updated_by] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_mcp_prompt] PRIMARY KEY ([id])
);

CREATE UNIQUE INDEX [ux_cms_mcp_instruction_site] ON [cms_mcp_instruction] ([site_id]) WHERE "type" IS NULL;

CREATE UNIQUE INDEX [ux_cms_mcp_instruction_site_type] ON [cms_mcp_instruction] ([site_id], [type]) WHERE "type" IS NOT NULL;

CREATE UNIQUE INDEX [ux_cms_mcp_prompt_site_name] ON [cms_mcp_prompt] ([site_id], [name]);

INSERT INTO [cms_mcp__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261110142623_InitialMcp', N'10.0.14');

COMMIT;
GO

