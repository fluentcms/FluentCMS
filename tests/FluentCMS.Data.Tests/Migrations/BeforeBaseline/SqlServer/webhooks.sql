-- Test data of BaselineTests, not shipped: what the migrations of stream webhooks ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
IF OBJECT_ID(N'[cms_webhooks__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms_webhooks__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms_webhooks__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_webhooks_delivery] (
    [id] uniqueidentifier NOT NULL,
    [delivery_id] uniqueidentifier NOT NULL,
    [webhook_id] uniqueidentifier NOT NULL,
    [event_id] uniqueidentifier NOT NULL,
    [event] nvarchar(64) NOT NULL,
    [attempt] int NOT NULL,
    [state] nvarchar(16) NOT NULL,
    [status_code] int NULL,
    [duration_ms] bigint NOT NULL,
    [request_body] nvarchar(max) NOT NULL,
    [response_excerpt] nvarchar(1024) NULL,
    [error] nvarchar(1000) NULL,
    [next_retry_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_webhooks_delivery] PRIMARY KEY ([id])
);

CREATE TABLE [cms_webhooks_subscription] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [name] nvarchar(100) NOT NULL,
    [url] nvarchar(2048) NOT NULL,
    [secret] nvarchar(max) NOT NULL,
    [events] nvarchar(max) NOT NULL,
    [types] nvarchar(max) NOT NULL,
    [headers] nvarchar(max) NOT NULL,
    [enabled] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] uniqueidentifier NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    [updated_by] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_webhooks_subscription] PRIMARY KEY ([id])
);

CREATE INDEX [ix_cms_webhooks_delivery_created_at] ON [cms_webhooks_delivery] ([created_at]);

CREATE INDEX [ix_cms_webhooks_delivery_webhook] ON [cms_webhooks_delivery] ([webhook_id], [created_at]);

CREATE UNIQUE INDEX [ux_cms_webhooks_delivery_attempt] ON [cms_webhooks_delivery] ([delivery_id], [attempt]);

CREATE UNIQUE INDEX [ux_cms_webhooks_subscription_site_name] ON [cms_webhooks_subscription] ([site_id], [name]);

INSERT INTO [cms_webhooks__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261108191354_InitialWebhooks', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [cms_webhooks_delivery] ADD [manual] bit NOT NULL DEFAULT CAST(0 AS bit);

CREATE UNIQUE INDEX [ux_cms_webhooks_delivery_in_flight] ON [cms_webhooks_delivery] ([webhook_id]) WHERE state = 'pending';

INSERT INTO [cms_webhooks__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261108191405_AddDeliveryClaim', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [cms_webhooks_subscription] ADD [previous_secret] nvarchar(max) NULL;

ALTER TABLE [cms_webhooks_subscription] ADD [previous_secret_expires_at] datetimeoffset NULL;

INSERT INTO [cms_webhooks__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261108191415_AddPreviousSecret', N'10.0.14');

COMMIT;
GO

