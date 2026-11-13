-- Test data of BaselineTests, not shipped: what the migrations of stream system ran on SQL Server before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
-- A GO follows each column a migration adds, since SQL Server compiles a batch before it runs, and EF runs each
-- command of a migration as a batch of its own.
IF OBJECT_ID(N'[cms__migrations]') IS NULL
BEGIN
    CREATE TABLE [cms__migrations] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_cms__migrations] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_document] (
    [id] uniqueidentifier NOT NULL,
    [document_id] uniqueidentifier NOT NULL,
    [locale] nvarchar(16) NOT NULL,
    [type] nvarchar(64) NOT NULL,
    [site_id] uniqueidentifier NULL,
    [status] nvarchar(16) NOT NULL,
    [draft_version] int NOT NULL,
    [published_version] int NULL,
    [published_at] datetimeoffset NULL,
    [first_published_at] datetimeoffset NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] uniqueidentifier NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] uniqueidentifier NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    [updated_by] uniqueidentifier NOT NULL,
    [schedule_publish_at] datetimeoffset NULL,
    [schedule_unpublish_at] datetimeoffset NULL,
    CONSTRAINT [PK_cms_document] PRIMARY KEY ([id])
);

CREATE TABLE [cms_reference] (
    [id] uniqueidentifier NOT NULL,
    [source_type] nvarchar(64) NOT NULL,
    [source_document_id] uniqueidentifier NOT NULL,
    [state] smallint NOT NULL,
    [locale] nvarchar(16) NOT NULL,
    [field] nvarchar(64) NOT NULL,
    [position] int NOT NULL,
    [target_type] nvarchar(64) NOT NULL,
    [target_id] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_reference] PRIMARY KEY ([id])
);

CREATE TABLE [cms_version] (
    [id] uniqueidentifier NOT NULL,
    [document_id] uniqueidentifier NOT NULL,
    [locale] nvarchar(16) NOT NULL,
    [number] int NOT NULL,
    [snapshot] nvarchar(max) NOT NULL,
    [status_after] nvarchar(16) NOT NULL,
    [author_id] uniqueidentifier NOT NULL,
    [origin] nvarchar(16) NOT NULL,
    [client_id] nvarchar(max) NULL,
    [message] nvarchar(max) NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_version] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_version_snapshot_json] CHECK (ISJSON([snapshot]) = 1)
);

CREATE UNIQUE INDEX [uq_cms_document_document_locale] ON [cms_document] ([document_id], [locale]);

CREATE INDEX [ix_cms_reference_target] ON [cms_reference] ([target_type], [target_id]);

CREATE UNIQUE INDEX [uq_cms_reference_source] ON [cms_reference] ([source_type], [source_document_id], [state], [locale], [field], [position]);

CREATE UNIQUE INDEX [uq_cms_version_document_locale_number] ON [cms_version] ([document_id], [locale], [number]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261102151146_InitialSystem', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_role] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NULL,
    [name] nvarchar(128) NOT NULL,
    [description] nvarchar(512) NULL,
    [built_in] bit NOT NULL,
    [permissions_version] bigint NOT NULL,
    CONSTRAINT [PK_cms_role] PRIMARY KEY ([id])
);

CREATE TABLE [cms_role_permission] (
    [id] uniqueidentifier NOT NULL,
    [role_id] uniqueidentifier NOT NULL,
    [permission] nvarchar(128) NOT NULL,
    [constraints] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_role_permission] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_role_permission_constraints_json] CHECK (ISJSON([constraints]) = 1),
    CONSTRAINT [FK_cms_role_permission_cms_role_role_id] FOREIGN KEY ([role_id]) REFERENCES [cms_role] ([id]) ON DELETE CASCADE
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'built_in', N'description', N'name', N'permissions_version', N'site_id') AND [object_id] = OBJECT_ID(N'[cms_role]'))
    SET IDENTITY_INSERT [cms_role] ON;
INSERT INTO [cms_role] ([id], [built_in], [description], [name], [permissions_version], [site_id])
VALUES ('00000000-0000-7000-8000-000000000001', CAST(1 AS bit), N'Holds every permission.', N'Super Admin', CAST(1 AS bigint), NULL),
('00000000-0000-7000-8000-000000000002', CAST(1 AS bit), N'Creates, edits, publishes and removes any content.', N'Editor', CAST(1 AS bigint), NULL),
('00000000-0000-7000-8000-000000000003', CAST(1 AS bit), N'Creates content, and edits or removes only their own entries.', N'Author', CAST(1 AS bigint), NULL),
('00000000-0000-7000-8000-000000000004', CAST(1 AS bit), N'The anonymous visitor. Grants here open public Delivery access.', N'Public', CAST(1 AS bigint), NULL),
('00000000-0000-7000-8000-000000000005', CAST(1 AS bit), N'The default role of a registered member.', N'Member', CAST(1 AS bigint), NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'built_in', N'description', N'name', N'permissions_version', N'site_id') AND [object_id] = OBJECT_ID(N'[cms_role]'))
    SET IDENTITY_INSERT [cms_role] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'constraints', N'permission', N'role_id') AND [object_id] = OBJECT_ID(N'[cms_role_permission]'))
    SET IDENTITY_INSERT [cms_role_permission] ON;
INSERT INTO [cms_role_permission] ([id], [constraints], [permission], [role_id])
VALUES ('00000000-0000-7000-8001-000000000001', NULL, N'*', '00000000-0000-7000-8000-000000000001'),
('00000000-0000-7000-8001-000000000002', NULL, N'content.*.create', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000003', NULL, N'content.*.read', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000004', NULL, N'content.*.update', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000005', NULL, N'content.*.delete', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000006', NULL, N'content.*.publish', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000007', NULL, N'content.*.unpublish', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000008', NULL, N'content.*.restore', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000009', NULL, N'schema.read', '00000000-0000-7000-8000-000000000002'),
('00000000-0000-7000-8001-000000000010', NULL, N'content.*.create', '00000000-0000-7000-8000-000000000003'),
('00000000-0000-7000-8001-000000000011', NULL, N'content.*.read', '00000000-0000-7000-8000-000000000003'),
('00000000-0000-7000-8001-000000000012', N'{"rowRule":"own-entries"}', N'content.*.update', '00000000-0000-7000-8000-000000000003'),
('00000000-0000-7000-8001-000000000013', N'{"rowRule":"own-entries"}', N'content.*.delete', '00000000-0000-7000-8000-000000000003'),
('00000000-0000-7000-8001-000000000014', NULL, N'schema.read', '00000000-0000-7000-8000-000000000003');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'constraints', N'permission', N'role_id') AND [object_id] = OBJECT_ID(N'[cms_role_permission]'))
    SET IDENTITY_INSERT [cms_role_permission] OFF;

CREATE UNIQUE INDEX [uq_cms_role_name] ON [cms_role] ([name]) WHERE site_id IS NULL;

CREATE UNIQUE INDEX [uq_cms_role_site_name] ON [cms_role] ([site_id], [name]) WHERE site_id IS NOT NULL;

CREATE INDEX [ix_cms_role_permission_role_permission] ON [cms_role_permission] ([role_id], [permission]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261102155506_AddRoles', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
UPDATE [cms_role] SET [description] = N'Creates content, and reads, edits or removes only their own entries.'
WHERE [id] = '00000000-0000-7000-8000-000000000003';
SELECT @@ROWCOUNT;


UPDATE [cms_role_permission] SET [constraints] = N'{"rowRule":"own-entries"}'
WHERE [id] = '00000000-0000-7000-8001-000000000011';
SELECT @@ROWCOUNT;


UPDATE cms_role SET permissions_version = permissions_version + 1 WHERE id = '00000000-0000-7000-8000-000000000003';

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261102171506_AuthorReadsOwnEntries', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_site] (
    [id] uniqueidentifier NOT NULL,
    [name] nvarchar(128) NOT NULL,
    [slug] nvarchar(64) NOT NULL,
    [default_locale] nvarchar(16) NOT NULL,
    [is_default] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_site] PRIMARY KEY ([id])
);

CREATE TABLE [cms_site_locale] (
    [site_id] uniqueidentifier NOT NULL,
    [code] nvarchar(16) NOT NULL,
    [position] int NOT NULL,
    [fallbacks] nvarchar(max) NULL,
    [enabled] bit NOT NULL,
    CONSTRAINT [PK_cms_site_locale] PRIMARY KEY ([site_id], [code]),
    CONSTRAINT [ck_cms_site_locale_fallbacks_json] CHECK (ISJSON([fallbacks]) = 1),
    CONSTRAINT [FK_cms_site_locale_cms_site_site_id] FOREIGN KEY ([site_id]) REFERENCES [cms_site] ([id]) ON DELETE CASCADE
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'created_at', N'default_locale', N'is_default', N'name', N'slug') AND [object_id] = OBJECT_ID(N'[cms_site]'))
    SET IDENTITY_INSERT [cms_site] ON;
INSERT INTO [cms_site] ([id], [created_at], [default_locale], [is_default], [name], [slug])
VALUES ('00000000-0000-7000-8002-000000000001', '2026-11-02T00:00:00.0000000+00:00', N'en', CAST(1 AS bit), N'Default', N'default');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'created_at', N'default_locale', N'is_default', N'name', N'slug') AND [object_id] = OBJECT_ID(N'[cms_site]'))
    SET IDENTITY_INSERT [cms_site] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'built_in', N'description', N'name', N'permissions_version', N'site_id') AND [object_id] = OBJECT_ID(N'[cms_role]'))
    SET IDENTITY_INSERT [cms_role] ON;
INSERT INTO [cms_role] ([id], [built_in], [description], [name], [permissions_version], [site_id])
VALUES ('00000000-0000-7000-8000-000000000006', CAST(1 AS bit), N'The anonymous visitor. Grants here open public Delivery access.', N'Public', CAST(1 AS bigint), '00000000-0000-7000-8002-000000000001');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'built_in', N'description', N'name', N'permissions_version', N'site_id') AND [object_id] = OBJECT_ID(N'[cms_role]'))
    SET IDENTITY_INSERT [cms_role] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'code', N'site_id', N'enabled', N'fallbacks', N'position') AND [object_id] = OBJECT_ID(N'[cms_site_locale]'))
    SET IDENTITY_INSERT [cms_site_locale] ON;
INSERT INTO [cms_site_locale] ([code], [site_id], [enabled], [fallbacks], [position])
VALUES (N'en', '00000000-0000-7000-8002-000000000001', CAST(1 AS bit), NULL, 0);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'code', N'site_id', N'enabled', N'fallbacks', N'position') AND [object_id] = OBJECT_ID(N'[cms_site_locale]'))
    SET IDENTITY_INSERT [cms_site_locale] OFF;

CREATE UNIQUE INDEX [uq_cms_site_slug] ON [cms_site] ([slug]);

ALTER TABLE [cms_role] ADD CONSTRAINT [FK_cms_role_cms_site_site_id] FOREIGN KEY ([site_id]) REFERENCES [cms_site] ([id]) ON DELETE CASCADE;

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261102181219_AddSites', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_job] (
    [id] uniqueidentifier NOT NULL,
    [type] nvarchar(128) NOT NULL,
    [payload] nvarchar(max) NOT NULL,
    [queue] nvarchar(64) NOT NULL,
    [site_id] uniqueidentifier NULL,
    [run_at] datetimeoffset NOT NULL,
    [priority] int NOT NULL,
    [status] nvarchar(16) NOT NULL,
    [attempts] int NOT NULL,
    [max_attempts] int NOT NULL,
    [lease_until] datetimeoffset NULL,
    [lease_owner] nvarchar(128) NULL,
    [last_error] nvarchar(2000) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] uniqueidentifier NULL,
    [origin] nvarchar(16) NOT NULL,
    [completed_at] datetimeoffset NULL,
    CONSTRAINT [PK_cms_job] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_job_payload_json] CHECK (ISJSON([payload]) = 1)
);

CREATE TABLE [cms_recurring_job] (
    [name] nvarchar(128) NOT NULL,
    [cron] nvarchar(128) NOT NULL,
    [type] nvarchar(128) NOT NULL,
    [payload] nvarchar(max) NOT NULL,
    [queue] nvarchar(64) NOT NULL,
    [next_run_at] datetimeoffset NOT NULL,
    [enabled] bit NOT NULL,
    CONSTRAINT [PK_cms_recurring_job] PRIMARY KEY ([name]),
    CONSTRAINT [ck_cms_recurring_job_payload_json] CHECK (ISJSON([payload]) = 1)
);

CREATE TABLE [cms_job_attempt] (
    [job_id] uniqueidentifier NOT NULL,
    [attempt] int NOT NULL,
    [started_at] datetimeoffset NOT NULL,
    [finished_at] datetimeoffset NULL,
    [outcome] nvarchar(16) NULL,
    [error] nvarchar(2000) NULL,
    CONSTRAINT [PK_cms_job_attempt] PRIMARY KEY ([job_id], [attempt]),
    CONSTRAINT [FK_cms_job_attempt_cms_job_job_id] FOREIGN KEY ([job_id]) REFERENCES [cms_job] ([id]) ON DELETE CASCADE
);

CREATE INDEX [ix_cms_job_claim] ON [cms_job] ([queue], [priority] DESC, [run_at], [id]) WHERE status IN ('pending', 'running');

CREATE INDEX [ix_cms_job_status_lease] ON [cms_job] ([status], [lease_until]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261102202445_AddJobs', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_outbox] (
    [id] uniqueidentifier NOT NULL,
    [event_id] uniqueidentifier NOT NULL,
    [event_type] nvarchar(256) NOT NULL,
    [handler] nvarchar(128) NOT NULL,
    [payload] nvarchar(max) NOT NULL,
    [site_id] uniqueidentifier NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [sequence] bigint NOT NULL,
    [status] nvarchar(16) NOT NULL,
    [attempts] int NOT NULL,
    [available_at] datetimeoffset NOT NULL,
    [lease_until] datetimeoffset NULL,
    [lease_owner] nvarchar(128) NULL,
    [last_error] nvarchar(2000) NULL,
    [delivered_at] datetimeoffset NULL,
    CONSTRAINT [PK_cms_outbox] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_outbox_payload_json] CHECK (ISJSON([payload]) = 1)
);

CREATE INDEX [ix_cms_outbox_delivered] ON [cms_outbox] ([delivered_at]) WHERE status = 'delivered';

CREATE INDEX [ix_cms_outbox_pending] ON [cms_outbox] ([sequence], [id]) WHERE status = 'pending';

CREATE UNIQUE INDEX [uq_cms_outbox_event_handler] ON [cms_outbox] ([event_id], [handler]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261102231110_AddOutbox', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_activity] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [actor_type] nvarchar(16) NOT NULL,
    [actor_id] uniqueidentifier NOT NULL,
    [actor_name] nvarchar(128) NOT NULL,
    [origin] nvarchar(16) NOT NULL,
    [client_id] nvarchar(128) NULL,
    [action] nvarchar(64) NOT NULL,
    [target_type] nvarchar(64) NULL,
    [target_id] nvarchar(128) NULL,
    [target_label] nvarchar(256) NULL,
    [locale] nvarchar(16) NULL,
    [details] nvarchar(max) NULL,
    [correlation_id] nvarchar(128) NOT NULL,
    [ip] nvarchar(45) NULL,
    CONSTRAINT [PK_cms_activity] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_activity_details_json] CHECK (ISJSON([details]) = 1)
);

CREATE INDEX [ix_cms_activity_actor] ON [cms_activity] ([actor_id]);

CREATE INDEX [ix_cms_activity_occurred] ON [cms_activity] ([occurred_at]);

CREATE INDEX [ix_cms_activity_site_occurred] ON [cms_activity] ([site_id], [occurred_at]);

CREATE INDEX [ix_cms_activity_target] ON [cms_activity] ([target_type], [target_id]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261103135403_AddActivity', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_schedule] (
    [id] uniqueidentifier NOT NULL,
    [document_id] uniqueidentifier NOT NULL,
    [locale] nvarchar(16) NOT NULL,
    [action] nvarchar(16) NOT NULL,
    [run_at_utc] datetimeoffset NOT NULL,
    [time_zone] nvarchar(64) NULL,
    [status] nvarchar(16) NOT NULL,
    [job_id] uniqueidentifier NULL,
    [version] int NOT NULL,
    [shared_hash] nvarchar(64) NULL,
    [created_by] uniqueidentifier NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [claims] nvarchar(max) NULL,
    [completed_at] datetimeoffset NULL,
    [error] nvarchar(2000) NULL,
    CONSTRAINT [PK_cms_schedule] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_schedule_claims_json] CHECK (ISJSON([claims]) = 1)
);

CREATE INDEX [ix_cms_schedule_document] ON [cms_schedule] ([document_id]);

CREATE UNIQUE INDEX [uq_cms_schedule_pending] ON [cms_schedule] ([document_id], [locale], [action]) WHERE status = 'pending';

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261104001152_AddSchedule', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_setting] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NULL,
    [key] nvarchar(64) NOT NULL,
    [json] nvarchar(max) NOT NULL,
    [version] int NOT NULL,
    [updated_at] datetimeoffset NOT NULL,
    [updated_by] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_setting] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_setting_json_json] CHECK (ISJSON([json]) = 1),
    CONSTRAINT [FK_cms_setting_cms_site_site_id] FOREIGN KEY ([site_id]) REFERENCES [cms_site] ([id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [uq_cms_setting_instance_key] ON [cms_setting] ([key]) WHERE site_id IS NULL;

CREATE UNIQUE INDEX [uq_cms_setting_site_key] ON [cms_setting] ([site_id], [key]) WHERE site_id IS NOT NULL;

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261104034418_AddSettings', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_schema_history] (
    [id] bigint NOT NULL IDENTITY,
    [hash] nvarchar(64) NOT NULL,
    [snapshot] nvarchar(max) NOT NULL,
    [applied_at] datetimeoffset NOT NULL,
    [fluentcms_version] nvarchar(64) NOT NULL,
    CONSTRAINT [PK_cms_schema_history] PRIMARY KEY ([id])
);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261104135445_AddSchemaHistory', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_invitation] (
    [id] uniqueidentifier NOT NULL,
    [email] nvarchar(256) NOT NULL,
    [normalized_email] nvarchar(256) NOT NULL,
    [token_hash] nvarchar(64) NOT NULL,
    [role_ids] nvarchar(max) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] uniqueidentifier NOT NULL,
    [created_by_kind] nvarchar(16) NOT NULL,
    [expires_at] datetimeoffset NOT NULL,
    [accepted_at] datetimeoffset NULL,
    [user_id] uniqueidentifier NULL,
    [revoked_at] datetimeoffset NULL,
    CONSTRAINT [PK_cms_invitation] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_invitation_role_ids_json] CHECK (ISJSON([role_ids]) = 1)
);

CREATE TABLE [cms_user] (
    [id] uniqueidentifier NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [deactivated_at] datetimeoffset NULL,
    [must_change_password] bit NOT NULL,
    [user_name] nvarchar(256) NULL,
    [normalized_user_name] nvarchar(256) NULL,
    [email] nvarchar(256) NULL,
    [normalized_email] nvarchar(256) NULL,
    [email_confirmed] bit NOT NULL,
    [password_hash] nvarchar(max) NULL,
    [security_stamp] nvarchar(64) NULL,
    [concurrency_stamp] nvarchar(64) NULL,
    [phone_number] nvarchar(32) NULL,
    [phone_number_confirmed] bit NOT NULL,
    [two_factor_enabled] bit NOT NULL,
    [lockout_end] datetimeoffset NULL,
    [lockout_enabled] bit NOT NULL,
    [access_failed_count] int NOT NULL,
    CONSTRAINT [PK_cms_user] PRIMARY KEY ([id])
);

CREATE TABLE [cms_user_claim] (
    [id] int NOT NULL IDENTITY,
    [user_id] uniqueidentifier NOT NULL,
    [claim_type] nvarchar(256) NULL,
    [claim_value] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_user_claim] PRIMARY KEY ([id]),
    CONSTRAINT [FK_cms_user_claim_cms_user_user_id] FOREIGN KEY ([user_id]) REFERENCES [cms_user] ([id]) ON DELETE CASCADE
);

CREATE TABLE [cms_user_login] (
    [login_provider] nvarchar(128) NOT NULL,
    [provider_key] nvarchar(128) NOT NULL,
    [provider_display_name] nvarchar(128) NULL,
    [user_id] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_user_login] PRIMARY KEY ([login_provider], [provider_key]),
    CONSTRAINT [FK_cms_user_login_cms_user_user_id] FOREIGN KEY ([user_id]) REFERENCES [cms_user] ([id]) ON DELETE CASCADE
);

CREATE TABLE [cms_user_role] (
    [user_id] uniqueidentifier NOT NULL,
    [role_id] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_user_role] PRIMARY KEY ([user_id], [role_id]),
    CONSTRAINT [FK_cms_user_role_cms_role_role_id] FOREIGN KEY ([role_id]) REFERENCES [cms_role] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_cms_user_role_cms_user_user_id] FOREIGN KEY ([user_id]) REFERENCES [cms_user] ([id]) ON DELETE CASCADE
);

CREATE TABLE [cms_user_token] (
    [user_id] uniqueidentifier NOT NULL,
    [login_provider] nvarchar(128) NOT NULL,
    [name] nvarchar(128) NOT NULL,
    [value] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_user_token] PRIMARY KEY ([user_id], [login_provider], [name]),
    CONSTRAINT [FK_cms_user_token_cms_user_user_id] FOREIGN KEY ([user_id]) REFERENCES [cms_user] ([id]) ON DELETE CASCADE
);

CREATE INDEX [ix_cms_invitation_normalized_email] ON [cms_invitation] ([normalized_email]);

CREATE UNIQUE INDEX [uq_cms_invitation_token_hash] ON [cms_invitation] ([token_hash]);

CREATE UNIQUE INDEX [uq_cms_user_normalized_email] ON [cms_user] ([normalized_email]) WHERE normalized_email IS NOT NULL;

CREATE UNIQUE INDEX [uq_cms_user_normalized_user_name] ON [cms_user] ([normalized_user_name]) WHERE [normalized_user_name] IS NOT NULL;

CREATE INDEX [ix_cms_user_claim_user] ON [cms_user_claim] ([user_id]);

CREATE INDEX [ix_cms_user_login_user] ON [cms_user_login] ([user_id]);

CREATE INDEX [ix_cms_user_role_role] ON [cms_user_role] ([role_id]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261105171342_AddUsers', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_api_key] (
    [id] uniqueidentifier NOT NULL,
    [name] nvarchar(128) NOT NULL,
    [class] nvarchar(16) NOT NULL,
    [key_prefix] nvarchar(16) NOT NULL,
    [key_hash] nvarchar(64) NOT NULL,
    [role_id] uniqueidentifier NOT NULL,
    [site_ids] nvarchar(max) NULL,
    [type_names] nvarchar(max) NULL,
    [expires_at] datetimeoffset NULL,
    [last_used_at] datetimeoffset NULL,
    [rate_limit] int NULL,
    [revoked_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_api_key] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cms_api_key_site_ids_json] CHECK (ISJSON([site_ids]) = 1),
    CONSTRAINT [ck_cms_api_key_type_names_json] CHECK (ISJSON([type_names]) = 1),
    CONSTRAINT [FK_cms_api_key_cms_role_role_id] FOREIGN KEY ([role_id]) REFERENCES [cms_role] ([id]) ON DELETE CASCADE
);

CREATE INDEX [ix_cms_api_key_key_prefix] ON [cms_api_key] ([key_prefix]);

CREATE INDEX [ix_cms_api_key_role] ON [cms_api_key] ([role_id]);

CREATE UNIQUE INDEX [uq_cms_api_key_key_hash] ON [cms_api_key] ([key_hash]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261105181547_AddApiKeys', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_data_protection_key] (
    [id] uniqueidentifier NOT NULL,
    [friendly_name] nvarchar(256) NULL,
    [xml] nvarchar(max) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_data_protection_key] PRIMARY KEY ([id])
);

CREATE TABLE [cms_oidc_application] (
    [id] uniqueidentifier NOT NULL,
    [application_type] nvarchar(50) NULL,
    [client_id] nvarchar(100) NULL,
    [client_secret] nvarchar(max) NULL,
    [client_type] nvarchar(50) NULL,
    [concurrency_token] nvarchar(50) NULL,
    [consent_type] nvarchar(50) NULL,
    [display_name] nvarchar(max) NULL,
    [display_names] nvarchar(max) NULL,
    [json_web_key_set] nvarchar(max) NULL,
    [permissions] nvarchar(max) NULL,
    [post_logout_redirect_uris] nvarchar(max) NULL,
    [properties] nvarchar(max) NULL,
    [redirect_uris] nvarchar(max) NULL,
    [requirements] nvarchar(max) NULL,
    [settings] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_oidc_application] PRIMARY KEY ([id])
);

CREATE TABLE [cms_oidc_scope] (
    [id] uniqueidentifier NOT NULL,
    [concurrency_token] nvarchar(50) NULL,
    [description] nvarchar(max) NULL,
    [descriptions] nvarchar(max) NULL,
    [display_name] nvarchar(max) NULL,
    [display_names] nvarchar(max) NULL,
    [name] nvarchar(200) NULL,
    [properties] nvarchar(max) NULL,
    [resources] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_oidc_scope] PRIMARY KEY ([id])
);

CREATE TABLE [cms_signing_key] (
    [id] uniqueidentifier NOT NULL,
    [use] nvarchar(16) NOT NULL,
    [algorithm] nvarchar(32) NOT NULL,
    [protected_key] nvarchar(max) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_signing_key] PRIMARY KEY ([id])
);

CREATE TABLE [cms_oidc_authorization] (
    [id] uniqueidentifier NOT NULL,
    [application_id] uniqueidentifier NULL,
    [concurrency_token] nvarchar(50) NULL,
    [creation_date] datetime2 NULL,
    [properties] nvarchar(max) NULL,
    [scopes] nvarchar(max) NULL,
    [status] nvarchar(50) NULL,
    [subject] nvarchar(400) NULL,
    [type] nvarchar(50) NULL,
    CONSTRAINT [PK_cms_oidc_authorization] PRIMARY KEY ([id]),
    CONSTRAINT [FK_cms_oidc_authorization_cms_oidc_application_application_id] FOREIGN KEY ([application_id]) REFERENCES [cms_oidc_application] ([id])
);

CREATE TABLE [cms_oidc_token] (
    [id] uniqueidentifier NOT NULL,
    [application_id] uniqueidentifier NULL,
    [authorization_id] uniqueidentifier NULL,
    [concurrency_token] nvarchar(50) NULL,
    [creation_date] datetime2 NULL,
    [expiration_date] datetime2 NULL,
    [payload] nvarchar(max) NULL,
    [properties] nvarchar(max) NULL,
    [redemption_date] datetime2 NULL,
    [reference_id] nvarchar(100) NULL,
    [status] nvarchar(50) NULL,
    [subject] nvarchar(400) NULL,
    [type] nvarchar(150) NULL,
    CONSTRAINT [PK_cms_oidc_token] PRIMARY KEY ([id]),
    CONSTRAINT [FK_cms_oidc_token_cms_oidc_application_application_id] FOREIGN KEY ([application_id]) REFERENCES [cms_oidc_application] ([id]),
    CONSTRAINT [FK_cms_oidc_token_cms_oidc_authorization_authorization_id] FOREIGN KEY ([authorization_id]) REFERENCES [cms_oidc_authorization] ([id])
);

CREATE UNIQUE INDEX [uq_cms_oidc_application_client_id] ON [cms_oidc_application] ([client_id]) WHERE [client_id] IS NOT NULL;

CREATE INDEX [ix_cms_oidc_authorization_application_id_status_subject_type] ON [cms_oidc_authorization] ([application_id], [status], [subject], [type]);

CREATE UNIQUE INDEX [uq_cms_oidc_scope_name] ON [cms_oidc_scope] ([name]) WHERE [name] IS NOT NULL;

CREATE INDEX [ix_cms_oidc_token_application_id_status_subject_type] ON [cms_oidc_token] ([application_id], [status], [subject], [type]);

CREATE INDEX [ix_cms_oidc_token_authorization_id] ON [cms_oidc_token] ([authorization_id]);

CREATE UNIQUE INDEX [uq_cms_oidc_token_reference_id] ON [cms_oidc_token] ([reference_id]) WHERE [reference_id] IS NOT NULL;

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261105215513_AddAuthorizationServer', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_lock] (
    [name] nvarchar(128) NOT NULL,
    [owner] nvarchar(128) NULL,
    [acquired_at] datetimeoffset NULL,
    [lease_until] datetimeoffset NULL,
    CONSTRAINT [PK_cms_lock] PRIMARY KEY ([name])
);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261106144334_AddLocks', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [cms_role] ADD [require_two_factor] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

UPDATE [cms_role] SET [require_two_factor] = CAST(0 AS bit)
WHERE [id] = '00000000-0000-7000-8000-000000000001';
SELECT @@ROWCOUNT;


UPDATE [cms_role] SET [require_two_factor] = CAST(0 AS bit)
WHERE [id] = '00000000-0000-7000-8000-000000000002';
SELECT @@ROWCOUNT;


UPDATE [cms_role] SET [require_two_factor] = CAST(0 AS bit)
WHERE [id] = '00000000-0000-7000-8000-000000000003';
SELECT @@ROWCOUNT;


UPDATE [cms_role] SET [require_two_factor] = CAST(0 AS bit)
WHERE [id] = '00000000-0000-7000-8000-000000000004';
SELECT @@ROWCOUNT;


UPDATE [cms_role] SET [require_two_factor] = CAST(0 AS bit)
WHERE [id] = '00000000-0000-7000-8000-000000000005';
SELECT @@ROWCOUNT;


UPDATE [cms_role] SET [require_two_factor] = CAST(0 AS bit)
WHERE [id] = '00000000-0000-7000-8000-000000000006';
SELECT @@ROWCOUNT;


INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261106160930_AddRoleTwoFactor', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_plugin_site] (
    [plugin] nvarchar(32) NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [enabled] bit NOT NULL,
    CONSTRAINT [PK_cms_plugin_site] PRIMARY KEY ([plugin], [site_id]),
    CONSTRAINT [FK_cms_plugin_site_cms_site_site_id] FOREIGN KEY ([site_id]) REFERENCES [cms_site] ([id]) ON DELETE CASCADE
);

CREATE INDEX [IX_cms_plugin_site_site_id] ON [cms_plugin_site] ([site_id]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261107024820_AddPluginSites', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE INDEX [ix_cms_document_trash] ON [cms_document] ([type], [deleted_at]) WHERE deleted_at IS NOT NULL;

CREATE INDEX [ix_cms_document_type_site] ON [cms_document] ([type], [site_id], [deleted_at]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261107180243_AddDocumentIndexes', N'10.0.14');

COMMIT;
GO

IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 0)
BEGIN
    IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), 0) = 0
    BEGIN
        DECLARE @message nvarchar(2048) = CONCAT(
            N'FluentCMS needs READ_COMMITTED_SNAPSHOT on in its SQL Server database, and the user that applies the migrations has no ALTER permission on the database to turn it on. Run ALTER DATABASE ',
            QUOTENAME(DB_NAME()),
            N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE as a user that has it, then apply the migrations again.');
        THROW 50000, @message, 1;
    END;

    ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
END;
GO

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261109212001_EnableReadCommittedSnapshot', N'10.0.14');
GO

BEGIN TRANSACTION;
ALTER TABLE [cms_api_key] ADD [allowed_addresses] nvarchar(max) NULL;
GO

ALTER TABLE [cms_api_key] ADD [previous_key_expires_at] datetimeoffset NULL;
GO

ALTER TABLE [cms_api_key] ADD [previous_key_hash] nvarchar(64) NULL;
GO

ALTER TABLE [cms_api_key] ADD [previous_key_prefix] nvarchar(16) NULL;
GO

CREATE INDEX [ix_cms_api_key_previous_key_prefix] ON [cms_api_key] ([previous_key_prefix]);

ALTER TABLE [cms_api_key] ADD CONSTRAINT [ck_cms_api_key_allowed_addresses_json] CHECK (ISJSON([allowed_addresses]) = 1);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261110033339_AddApiKeyAddressesAndRotation', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_idempotency_key] (
    [id] uniqueidentifier NOT NULL,
    [site_id] uniqueidentifier NOT NULL,
    [principal_kind] nvarchar(16) NOT NULL,
    [principal_id] uniqueidentifier NOT NULL,
    [key_hash] nvarchar(64) NOT NULL,
    [request_hash] nvarchar(64) NOT NULL,
    [owner] uniqueidentifier NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [lease_until] datetimeoffset NOT NULL,
    [expires_at] datetimeoffset NOT NULL,
    [response_status] int NULL,
    [response_headers] nvarchar(max) NULL,
    [response_body] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_idempotency_key] PRIMARY KEY ([id])
);

CREATE INDEX [ix_cms_idempotency_key_expires_at] ON [cms_idempotency_key] ([expires_at]);

CREATE UNIQUE INDEX [uq_cms_idempotency_key_scope] ON [cms_idempotency_key] ([site_id], [principal_kind], [principal_id], [key_hash]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261110041403_AddIdempotencyKeys', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
CREATE TABLE [cms_member] (
    [id] uniqueidentifier NOT NULL,
    [display_name] nvarchar(128) NULL,
    [created_at] datetimeoffset NOT NULL,
    [deactivated_at] datetimeoffset NULL,
    [last_login_at] datetimeoffset NULL,
    [email] nvarchar(256) NOT NULL,
    [normalized_email] nvarchar(256) NOT NULL,
    [email_confirmed] bit NOT NULL,
    [password_hash] nvarchar(max) NULL,
    [security_stamp] nvarchar(64) NULL,
    [concurrency_stamp] nvarchar(64) NULL,
    [lockout_end] datetimeoffset NULL,
    [access_failed_count] int NOT NULL,
    CONSTRAINT [PK_cms_member] PRIMARY KEY ([id])
);

CREATE TABLE [cms_member_group] (
    [id] uniqueidentifier NOT NULL,
    [name] nvarchar(64) NOT NULL,
    [description] nvarchar(512) NULL,
    [role_id] uniqueidentifier NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    CONSTRAINT [PK_cms_member_group] PRIMARY KEY ([id]),
    CONSTRAINT [FK_cms_member_group_cms_role_role_id] FOREIGN KEY ([role_id]) REFERENCES [cms_role] ([id]) ON DELETE CASCADE
);

CREATE TABLE [cms_member_login] (
    [login_provider] nvarchar(128) NOT NULL,
    [provider_key] nvarchar(128) NOT NULL,
    [provider_display_name] nvarchar(128) NULL,
    [member_id] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_member_login] PRIMARY KEY ([login_provider], [provider_key]),
    CONSTRAINT [FK_cms_member_login_cms_member_member_id] FOREIGN KEY ([member_id]) REFERENCES [cms_member] ([id]) ON DELETE CASCADE
);

CREATE TABLE [cms_member_token] (
    [member_id] uniqueidentifier NOT NULL,
    [login_provider] nvarchar(128) NOT NULL,
    [name] nvarchar(128) NOT NULL,
    [value] nvarchar(max) NULL,
    CONSTRAINT [PK_cms_member_token] PRIMARY KEY ([member_id], [login_provider], [name]),
    CONSTRAINT [FK_cms_member_token_cms_member_member_id] FOREIGN KEY ([member_id]) REFERENCES [cms_member] ([id]) ON DELETE CASCADE
);

CREATE TABLE [cms_member_group_member] (
    [group_id] uniqueidentifier NOT NULL,
    [member_id] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_cms_member_group_member] PRIMARY KEY ([group_id], [member_id]),
    CONSTRAINT [FK_cms_member_group_member_cms_member_group_group_id] FOREIGN KEY ([group_id]) REFERENCES [cms_member_group] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_cms_member_group_member_cms_member_member_id] FOREIGN KEY ([member_id]) REFERENCES [cms_member] ([id]) ON DELETE CASCADE
);

CREATE INDEX [ix_cms_member_email_confirmed_created_at] ON [cms_member] ([email_confirmed], [created_at]);

CREATE UNIQUE INDEX [uq_cms_member_normalized_email] ON [cms_member] ([normalized_email]);

CREATE INDEX [ix_cms_member_group_role] ON [cms_member_group] ([role_id]);

CREATE UNIQUE INDEX [uq_cms_member_group_name] ON [cms_member_group] ([name]);

CREATE INDEX [ix_cms_member_group_member_member] ON [cms_member_group_member] ([member_id]);

CREATE INDEX [ix_cms_member_login_member] ON [cms_member_login] ([member_id]);

INSERT INTO [cms__migrations] ([MigrationId], [ProductVersion])
VALUES (N'20261111061719_AddMembers', N'10.0.14');

COMMIT;
GO

