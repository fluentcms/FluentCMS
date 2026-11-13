-- Test data of BaselineTests, not shipped: what the migrations of stream system ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS "cms__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_document" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_document" PRIMARY KEY,
    "document_id" TEXT NOT NULL,
    "locale" TEXT NOT NULL,
    "type" TEXT NOT NULL,
    "site_id" TEXT NULL,
    "status" TEXT NOT NULL,
    "draft_version" INTEGER NOT NULL,
    "published_version" INTEGER NULL,
    "published_at" INTEGER NULL,
    "first_published_at" INTEGER NULL,
    "deleted_at" INTEGER NULL,
    "deleted_by" TEXT NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NOT NULL,
    "updated_at" INTEGER NOT NULL,
    "updated_by" TEXT NOT NULL,
    "schedule_publish_at" INTEGER NULL,
    "schedule_unpublish_at" INTEGER NULL
);

CREATE TABLE "cms_reference" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_reference" PRIMARY KEY,
    "source_type" TEXT NOT NULL,
    "source_document_id" TEXT NOT NULL,
    "state" INTEGER NOT NULL,
    "locale" TEXT NOT NULL,
    "field" TEXT NOT NULL,
    "position" INTEGER NOT NULL,
    "target_type" TEXT NOT NULL,
    "target_id" TEXT NOT NULL
);

CREATE TABLE "cms_version" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_version" PRIMARY KEY,
    "document_id" TEXT NOT NULL,
    "locale" TEXT NOT NULL,
    "number" INTEGER NOT NULL,
    "snapshot" TEXT NOT NULL,
    "status_after" TEXT NOT NULL,
    "author_id" TEXT NOT NULL,
    "origin" TEXT NOT NULL,
    "client_id" TEXT NULL,
    "message" TEXT NULL,
    "created_at" INTEGER NOT NULL,
    CONSTRAINT "ck_cms_version_snapshot_json" CHECK (json_valid("snapshot"))
);

CREATE UNIQUE INDEX "uq_cms_document_document_locale" ON "cms_document" ("document_id", "locale");

CREATE INDEX "ix_cms_reference_target" ON "cms_reference" ("target_type", "target_id");

CREATE UNIQUE INDEX "uq_cms_reference_source" ON "cms_reference" ("source_type", "source_document_id", "state", "locale", "field", "position");

CREATE UNIQUE INDEX "uq_cms_version_document_locale_number" ON "cms_version" ("document_id", "locale", "number");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261102151133_InitialSystem', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_role" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_role" PRIMARY KEY,
    "site_id" TEXT NULL,
    "name" TEXT NOT NULL,
    "description" TEXT NULL,
    "built_in" INTEGER NOT NULL,
    "permissions_version" INTEGER NOT NULL
);

CREATE TABLE "cms_role_permission" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_role_permission" PRIMARY KEY,
    "role_id" TEXT NOT NULL,
    "permission" TEXT NOT NULL,
    "constraints" TEXT NULL,
    CONSTRAINT "ck_cms_role_permission_constraints_json" CHECK (json_valid("constraints")),
    CONSTRAINT "FK_cms_role_permission_cms_role_role_id" FOREIGN KEY ("role_id") REFERENCES "cms_role" ("id") ON DELETE CASCADE
);

INSERT INTO "cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
VALUES ('00000000-0000-7000-8000-000000000001', 1, 'Holds every permission.', 'Super Admin', 1, NULL);
SELECT changes();

INSERT INTO "cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
VALUES ('00000000-0000-7000-8000-000000000002', 1, 'Creates, edits, publishes and removes any content.', 'Editor', 1, NULL);
SELECT changes();

INSERT INTO "cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
VALUES ('00000000-0000-7000-8000-000000000003', 1, 'Creates content, and edits or removes only their own entries.', 'Author', 1, NULL);
SELECT changes();

INSERT INTO "cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
VALUES ('00000000-0000-7000-8000-000000000004', 1, 'The anonymous visitor. Grants here open public Delivery access.', 'Public', 1, NULL);
SELECT changes();

INSERT INTO "cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
VALUES ('00000000-0000-7000-8000-000000000005', 1, 'The default role of a registered member.', 'Member', 1, NULL);
SELECT changes();


INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000001', NULL, '*', '00000000-0000-7000-8000-000000000001');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000002', NULL, 'content.*.create', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000003', NULL, 'content.*.read', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000004', NULL, 'content.*.update', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000005', NULL, 'content.*.delete', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000006', NULL, 'content.*.publish', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000007', NULL, 'content.*.unpublish', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000008', NULL, 'content.*.restore', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000009', NULL, 'schema.read', '00000000-0000-7000-8000-000000000002');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000010', NULL, 'content.*.create', '00000000-0000-7000-8000-000000000003');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000011', NULL, 'content.*.read', '00000000-0000-7000-8000-000000000003');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000012', '{"rowRule":"own-entries"}', 'content.*.update', '00000000-0000-7000-8000-000000000003');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000013', '{"rowRule":"own-entries"}', 'content.*.delete', '00000000-0000-7000-8000-000000000003');
SELECT changes();

INSERT INTO "cms_role_permission" ("id", "constraints", "permission", "role_id")
VALUES ('00000000-0000-7000-8001-000000000014', NULL, 'schema.read', '00000000-0000-7000-8000-000000000003');
SELECT changes();


CREATE UNIQUE INDEX "uq_cms_role_name" ON "cms_role" ("name") WHERE site_id IS NULL;

CREATE UNIQUE INDEX "uq_cms_role_site_name" ON "cms_role" ("site_id", "name") WHERE site_id IS NOT NULL;

CREATE INDEX "ix_cms_role_permission_role_permission" ON "cms_role_permission" ("role_id", "permission");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261102155453_AddRoles', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
UPDATE "cms_role" SET "description" = 'Creates content, and reads, edits or removes only their own entries.'
WHERE "id" = '00000000-0000-7000-8000-000000000003';
SELECT changes();


UPDATE "cms_role_permission" SET "constraints" = '{"rowRule":"own-entries"}'
WHERE "id" = '00000000-0000-7000-8001-000000000011';
SELECT changes();


UPDATE cms_role SET permissions_version = permissions_version + 1 WHERE id = '00000000-0000-7000-8000-000000000003';

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261102171454_AuthorReadsOwnEntries', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_site" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_site" PRIMARY KEY,
    "name" TEXT NOT NULL,
    "slug" TEXT NOT NULL,
    "default_locale" TEXT NOT NULL,
    "is_default" INTEGER NOT NULL,
    "created_at" INTEGER NOT NULL
);

CREATE TABLE "cms_site_locale" (
    "site_id" TEXT NOT NULL,
    "code" TEXT NOT NULL,
    "position" INTEGER NOT NULL,
    "fallbacks" TEXT NULL,
    "enabled" INTEGER NOT NULL,
    CONSTRAINT "PK_cms_site_locale" PRIMARY KEY ("site_id", "code"),
    CONSTRAINT "ck_cms_site_locale_fallbacks_json" CHECK (json_valid("fallbacks")),
    CONSTRAINT "FK_cms_site_locale_cms_site_site_id" FOREIGN KEY ("site_id") REFERENCES "cms_site" ("id") ON DELETE CASCADE
);

INSERT INTO "cms_site" ("id", "created_at", "default_locale", "is_default", "name", "slug")
VALUES ('00000000-0000-7000-8002-000000000001', 639291744000000000, 'en', 1, 'Default', 'default');
SELECT changes();


INSERT INTO "cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
VALUES ('00000000-0000-7000-8000-000000000006', 1, 'The anonymous visitor. Grants here open public Delivery access.', 'Public', 1, '00000000-0000-7000-8002-000000000001');
SELECT changes();


INSERT INTO "cms_site_locale" ("code", "site_id", "enabled", "fallbacks", "position")
VALUES ('en', '00000000-0000-7000-8002-000000000001', 1, NULL, 0);
SELECT changes();


CREATE UNIQUE INDEX "uq_cms_site_slug" ON "cms_site" ("slug");

CREATE TABLE "ef_temp_cms_role" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_role" PRIMARY KEY,
    "built_in" INTEGER NOT NULL,
    "description" TEXT NULL,
    "name" TEXT NOT NULL,
    "permissions_version" INTEGER NOT NULL,
    "site_id" TEXT NULL,
    CONSTRAINT "FK_cms_role_cms_site_site_id" FOREIGN KEY ("site_id") REFERENCES "cms_site" ("id") ON DELETE CASCADE
);

INSERT INTO "ef_temp_cms_role" ("id", "built_in", "description", "name", "permissions_version", "site_id")
SELECT "id", "built_in", "description", "name", "permissions_version", "site_id"
FROM "cms_role";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "cms_role";

ALTER TABLE "ef_temp_cms_role" RENAME TO "cms_role";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "uq_cms_role_name" ON "cms_role" ("name") WHERE site_id IS NULL;

CREATE UNIQUE INDEX "uq_cms_role_site_name" ON "cms_role" ("site_id", "name") WHERE site_id IS NOT NULL;

COMMIT;

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261102181206_AddSites', '10.0.14');

BEGIN TRANSACTION;
CREATE TABLE "cms_job" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_job" PRIMARY KEY,
    "type" TEXT NOT NULL,
    "payload" TEXT NOT NULL,
    "queue" TEXT NOT NULL,
    "site_id" TEXT NULL,
    "run_at" INTEGER NOT NULL,
    "priority" INTEGER NOT NULL,
    "status" TEXT NOT NULL,
    "attempts" INTEGER NOT NULL,
    "max_attempts" INTEGER NOT NULL,
    "lease_until" INTEGER NULL,
    "lease_owner" TEXT NULL,
    "last_error" TEXT NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NULL,
    "origin" TEXT NOT NULL,
    "completed_at" INTEGER NULL,
    CONSTRAINT "ck_cms_job_payload_json" CHECK (json_valid("payload"))
);

CREATE TABLE "cms_recurring_job" (
    "name" TEXT NOT NULL CONSTRAINT "PK_cms_recurring_job" PRIMARY KEY,
    "cron" TEXT NOT NULL,
    "type" TEXT NOT NULL,
    "payload" TEXT NOT NULL,
    "queue" TEXT NOT NULL,
    "next_run_at" INTEGER NOT NULL,
    "enabled" INTEGER NOT NULL,
    CONSTRAINT "ck_cms_recurring_job_payload_json" CHECK (json_valid("payload"))
);

CREATE TABLE "cms_job_attempt" (
    "job_id" TEXT NOT NULL,
    "attempt" INTEGER NOT NULL,
    "started_at" INTEGER NOT NULL,
    "finished_at" INTEGER NULL,
    "outcome" TEXT NULL,
    "error" TEXT NULL,
    CONSTRAINT "PK_cms_job_attempt" PRIMARY KEY ("job_id", "attempt"),
    CONSTRAINT "FK_cms_job_attempt_cms_job_job_id" FOREIGN KEY ("job_id") REFERENCES "cms_job" ("id") ON DELETE CASCADE
);

CREATE INDEX "ix_cms_job_claim" ON "cms_job" ("queue", "priority" DESC, "run_at", "id") WHERE status IN ('pending', 'running');

CREATE INDEX "ix_cms_job_status_lease" ON "cms_job" ("status", "lease_until");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261102202434_AddJobs', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_outbox" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_outbox" PRIMARY KEY,
    "event_id" TEXT NOT NULL,
    "event_type" TEXT NOT NULL,
    "handler" TEXT NOT NULL,
    "payload" TEXT NOT NULL,
    "site_id" TEXT NULL,
    "occurred_at" INTEGER NOT NULL,
    "sequence" INTEGER NOT NULL,
    "status" TEXT NOT NULL,
    "attempts" INTEGER NOT NULL,
    "available_at" INTEGER NOT NULL,
    "lease_until" INTEGER NULL,
    "lease_owner" TEXT NULL,
    "last_error" TEXT NULL,
    "delivered_at" INTEGER NULL,
    CONSTRAINT "ck_cms_outbox_payload_json" CHECK (json_valid("payload"))
);

CREATE INDEX "ix_cms_outbox_delivered" ON "cms_outbox" ("delivered_at") WHERE status = 'delivered';

CREATE INDEX "ix_cms_outbox_pending" ON "cms_outbox" ("sequence", "id") WHERE status = 'pending';

CREATE UNIQUE INDEX "uq_cms_outbox_event_handler" ON "cms_outbox" ("event_id", "handler");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261102231057_AddOutbox', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_activity" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_activity" PRIMARY KEY,
    "site_id" TEXT NULL,
    "occurred_at" INTEGER NOT NULL,
    "actor_type" TEXT NOT NULL,
    "actor_id" TEXT NOT NULL,
    "actor_name" TEXT NOT NULL,
    "origin" TEXT NOT NULL,
    "client_id" TEXT NULL,
    "action" TEXT NOT NULL,
    "target_type" TEXT NULL,
    "target_id" TEXT NULL,
    "target_label" TEXT NULL,
    "locale" TEXT NULL,
    "details" TEXT NULL,
    "correlation_id" TEXT NOT NULL,
    "ip" TEXT NULL,
    CONSTRAINT "ck_cms_activity_details_json" CHECK (json_valid("details"))
);

CREATE INDEX "ix_cms_activity_actor" ON "cms_activity" ("actor_id");

CREATE INDEX "ix_cms_activity_occurred" ON "cms_activity" ("occurred_at");

CREATE INDEX "ix_cms_activity_site_occurred" ON "cms_activity" ("site_id", "occurred_at");

CREATE INDEX "ix_cms_activity_target" ON "cms_activity" ("target_type", "target_id");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261103135351_AddActivity', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_schedule" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_schedule" PRIMARY KEY,
    "document_id" TEXT NOT NULL,
    "locale" TEXT NOT NULL,
    "action" TEXT NOT NULL,
    "run_at_utc" INTEGER NOT NULL,
    "time_zone" TEXT NULL,
    "status" TEXT NOT NULL,
    "job_id" TEXT NULL,
    "version" INTEGER NOT NULL,
    "shared_hash" TEXT NULL,
    "created_by" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL,
    "claims" TEXT NULL,
    "completed_at" INTEGER NULL,
    "error" TEXT NULL,
    CONSTRAINT "ck_cms_schedule_claims_json" CHECK (json_valid("claims"))
);

CREATE INDEX "ix_cms_schedule_document" ON "cms_schedule" ("document_id");

CREATE UNIQUE INDEX "uq_cms_schedule_pending" ON "cms_schedule" ("document_id", "locale", "action") WHERE status = 'pending';

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261104001141_AddSchedule', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_setting" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_setting" PRIMARY KEY,
    "site_id" TEXT NULL,
    "key" TEXT NOT NULL,
    "json" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "updated_at" INTEGER NOT NULL,
    "updated_by" TEXT NOT NULL,
    CONSTRAINT "ck_cms_setting_json_json" CHECK (json_valid("json")),
    CONSTRAINT "FK_cms_setting_cms_site_site_id" FOREIGN KEY ("site_id") REFERENCES "cms_site" ("id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "uq_cms_setting_instance_key" ON "cms_setting" ("key") WHERE site_id IS NULL;

CREATE UNIQUE INDEX "uq_cms_setting_site_key" ON "cms_setting" ("site_id", "key") WHERE site_id IS NOT NULL;

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261104034341_AddSettings', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_schema_history" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_cms_schema_history" PRIMARY KEY AUTOINCREMENT,
    "hash" TEXT NOT NULL,
    "snapshot" TEXT NOT NULL,
    "applied_at" INTEGER NOT NULL,
    "fluentcms_version" TEXT NOT NULL
);

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261104135431_AddSchemaHistory', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_invitation" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_invitation" PRIMARY KEY,
    "email" TEXT NOT NULL,
    "normalized_email" TEXT NOT NULL,
    "token_hash" TEXT NOT NULL,
    "role_ids" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NOT NULL,
    "created_by_kind" TEXT NOT NULL,
    "expires_at" INTEGER NOT NULL,
    "accepted_at" INTEGER NULL,
    "user_id" TEXT NULL,
    "revoked_at" INTEGER NULL,
    CONSTRAINT "ck_cms_invitation_role_ids_json" CHECK (json_valid("role_ids"))
);

CREATE TABLE "cms_user" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_user" PRIMARY KEY,
    "created_at" INTEGER NOT NULL,
    "deactivated_at" INTEGER NULL,
    "must_change_password" INTEGER NOT NULL,
    "user_name" TEXT NULL,
    "normalized_user_name" TEXT NULL,
    "email" TEXT NULL,
    "normalized_email" TEXT NULL,
    "email_confirmed" INTEGER NOT NULL,
    "password_hash" TEXT NULL,
    "security_stamp" TEXT NULL,
    "concurrency_stamp" TEXT NULL,
    "phone_number" TEXT NULL,
    "phone_number_confirmed" INTEGER NOT NULL,
    "two_factor_enabled" INTEGER NOT NULL,
    "lockout_end" INTEGER NULL,
    "lockout_enabled" INTEGER NOT NULL,
    "access_failed_count" INTEGER NOT NULL
);

CREATE TABLE "cms_user_claim" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_cms_user_claim" PRIMARY KEY AUTOINCREMENT,
    "user_id" TEXT NOT NULL,
    "claim_type" TEXT NULL,
    "claim_value" TEXT NULL,
    CONSTRAINT "FK_cms_user_claim_cms_user_user_id" FOREIGN KEY ("user_id") REFERENCES "cms_user" ("id") ON DELETE CASCADE
);

CREATE TABLE "cms_user_login" (
    "login_provider" TEXT NOT NULL,
    "provider_key" TEXT NOT NULL,
    "provider_display_name" TEXT NULL,
    "user_id" TEXT NOT NULL,
    CONSTRAINT "PK_cms_user_login" PRIMARY KEY ("login_provider", "provider_key"),
    CONSTRAINT "FK_cms_user_login_cms_user_user_id" FOREIGN KEY ("user_id") REFERENCES "cms_user" ("id") ON DELETE CASCADE
);

CREATE TABLE "cms_user_role" (
    "user_id" TEXT NOT NULL,
    "role_id" TEXT NOT NULL,
    CONSTRAINT "PK_cms_user_role" PRIMARY KEY ("user_id", "role_id"),
    CONSTRAINT "FK_cms_user_role_cms_role_role_id" FOREIGN KEY ("role_id") REFERENCES "cms_role" ("id") ON DELETE CASCADE,
    CONSTRAINT "FK_cms_user_role_cms_user_user_id" FOREIGN KEY ("user_id") REFERENCES "cms_user" ("id") ON DELETE CASCADE
);

CREATE TABLE "cms_user_token" (
    "user_id" TEXT NOT NULL,
    "login_provider" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "value" TEXT NULL,
    CONSTRAINT "PK_cms_user_token" PRIMARY KEY ("user_id", "login_provider", "name"),
    CONSTRAINT "FK_cms_user_token_cms_user_user_id" FOREIGN KEY ("user_id") REFERENCES "cms_user" ("id") ON DELETE CASCADE
);

CREATE INDEX "ix_cms_invitation_normalized_email" ON "cms_invitation" ("normalized_email");

CREATE UNIQUE INDEX "uq_cms_invitation_token_hash" ON "cms_invitation" ("token_hash");

CREATE UNIQUE INDEX "uq_cms_user_normalized_email" ON "cms_user" ("normalized_email") WHERE normalized_email IS NOT NULL;

CREATE UNIQUE INDEX "uq_cms_user_normalized_user_name" ON "cms_user" ("normalized_user_name");

CREATE INDEX "ix_cms_user_claim_user" ON "cms_user_claim" ("user_id");

CREATE INDEX "ix_cms_user_login_user" ON "cms_user_login" ("user_id");

CREATE INDEX "ix_cms_user_role_role" ON "cms_user_role" ("role_id");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261105171329_AddUsers', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_api_key" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_api_key" PRIMARY KEY,
    "name" TEXT NOT NULL,
    "class" TEXT NOT NULL,
    "key_prefix" TEXT NOT NULL,
    "key_hash" TEXT NOT NULL,
    "role_id" TEXT NOT NULL,
    "site_ids" TEXT NULL,
    "type_names" TEXT NULL,
    "expires_at" INTEGER NULL,
    "last_used_at" INTEGER NULL,
    "rate_limit" INTEGER NULL,
    "revoked_at" INTEGER NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NOT NULL,
    CONSTRAINT "ck_cms_api_key_site_ids_json" CHECK (json_valid("site_ids")),
    CONSTRAINT "ck_cms_api_key_type_names_json" CHECK (json_valid("type_names")),
    CONSTRAINT "FK_cms_api_key_cms_role_role_id" FOREIGN KEY ("role_id") REFERENCES "cms_role" ("id") ON DELETE CASCADE
);

CREATE INDEX "ix_cms_api_key_key_prefix" ON "cms_api_key" ("key_prefix");

CREATE INDEX "ix_cms_api_key_role" ON "cms_api_key" ("role_id");

CREATE UNIQUE INDEX "uq_cms_api_key_key_hash" ON "cms_api_key" ("key_hash");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261105181531_AddApiKeys', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_data_protection_key" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_data_protection_key" PRIMARY KEY,
    "friendly_name" TEXT NULL,
    "xml" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL
);

CREATE TABLE "cms_oidc_application" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_oidc_application" PRIMARY KEY,
    "application_type" TEXT NULL,
    "client_id" TEXT NULL,
    "client_secret" TEXT NULL,
    "client_type" TEXT NULL,
    "concurrency_token" TEXT NULL,
    "consent_type" TEXT NULL,
    "display_name" TEXT NULL,
    "display_names" TEXT NULL,
    "json_web_key_set" TEXT NULL,
    "permissions" TEXT NULL,
    "post_logout_redirect_uris" TEXT NULL,
    "properties" TEXT NULL,
    "redirect_uris" TEXT NULL,
    "requirements" TEXT NULL,
    "settings" TEXT NULL
);

CREATE TABLE "cms_oidc_scope" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_oidc_scope" PRIMARY KEY,
    "concurrency_token" TEXT NULL,
    "description" TEXT NULL,
    "descriptions" TEXT NULL,
    "display_name" TEXT NULL,
    "display_names" TEXT NULL,
    "name" TEXT NULL,
    "properties" TEXT NULL,
    "resources" TEXT NULL
);

CREATE TABLE "cms_signing_key" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_signing_key" PRIMARY KEY,
    "use" TEXT NOT NULL,
    "algorithm" TEXT NOT NULL,
    "protected_key" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL
);

CREATE TABLE "cms_oidc_authorization" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_oidc_authorization" PRIMARY KEY,
    "application_id" TEXT NULL,
    "concurrency_token" TEXT NULL,
    "creation_date" TEXT NULL,
    "properties" TEXT NULL,
    "scopes" TEXT NULL,
    "status" TEXT NULL,
    "subject" TEXT NULL,
    "type" TEXT NULL,
    CONSTRAINT "FK_cms_oidc_authorization_cms_oidc_application_application_id" FOREIGN KEY ("application_id") REFERENCES "cms_oidc_application" ("id")
);

CREATE TABLE "cms_oidc_token" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_oidc_token" PRIMARY KEY,
    "application_id" TEXT NULL,
    "authorization_id" TEXT NULL,
    "concurrency_token" TEXT NULL,
    "creation_date" TEXT NULL,
    "expiration_date" TEXT NULL,
    "payload" TEXT NULL,
    "properties" TEXT NULL,
    "redemption_date" TEXT NULL,
    "reference_id" TEXT NULL,
    "status" TEXT NULL,
    "subject" TEXT NULL,
    "type" TEXT NULL,
    CONSTRAINT "FK_cms_oidc_token_cms_oidc_application_application_id" FOREIGN KEY ("application_id") REFERENCES "cms_oidc_application" ("id"),
    CONSTRAINT "FK_cms_oidc_token_cms_oidc_authorization_authorization_id" FOREIGN KEY ("authorization_id") REFERENCES "cms_oidc_authorization" ("id")
);

CREATE UNIQUE INDEX "uq_cms_oidc_application_client_id" ON "cms_oidc_application" ("client_id");

CREATE INDEX "ix_cms_oidc_authorization_application_id_status_subject_type" ON "cms_oidc_authorization" ("application_id", "status", "subject", "type");

CREATE UNIQUE INDEX "uq_cms_oidc_scope_name" ON "cms_oidc_scope" ("name");

CREATE INDEX "ix_cms_oidc_token_application_id_status_subject_type" ON "cms_oidc_token" ("application_id", "status", "subject", "type");

CREATE INDEX "ix_cms_oidc_token_authorization_id" ON "cms_oidc_token" ("authorization_id");

CREATE UNIQUE INDEX "uq_cms_oidc_token_reference_id" ON "cms_oidc_token" ("reference_id");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261105215329_AddAuthorizationServer', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_lock" (
    "name" TEXT NOT NULL CONSTRAINT "PK_cms_lock" PRIMARY KEY,
    "owner" TEXT NULL,
    "acquired_at" INTEGER NULL,
    "lease_until" INTEGER NULL
);

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261106144317_AddLocks', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "cms_role" ADD "require_two_factor" INTEGER NOT NULL DEFAULT 0;

UPDATE "cms_role" SET "require_two_factor" = 0
WHERE "id" = '00000000-0000-7000-8000-000000000001';
SELECT changes();


UPDATE "cms_role" SET "require_two_factor" = 0
WHERE "id" = '00000000-0000-7000-8000-000000000002';
SELECT changes();


UPDATE "cms_role" SET "require_two_factor" = 0
WHERE "id" = '00000000-0000-7000-8000-000000000003';
SELECT changes();


UPDATE "cms_role" SET "require_two_factor" = 0
WHERE "id" = '00000000-0000-7000-8000-000000000004';
SELECT changes();


UPDATE "cms_role" SET "require_two_factor" = 0
WHERE "id" = '00000000-0000-7000-8000-000000000005';
SELECT changes();


UPDATE "cms_role" SET "require_two_factor" = 0
WHERE "id" = '00000000-0000-7000-8000-000000000006';
SELECT changes();


INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261106160910_AddRoleTwoFactor', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_plugin_site" (
    "plugin" TEXT NOT NULL,
    "site_id" TEXT NOT NULL,
    "enabled" INTEGER NOT NULL,
    CONSTRAINT "PK_cms_plugin_site" PRIMARY KEY ("plugin", "site_id"),
    CONSTRAINT "FK_cms_plugin_site_cms_site_site_id" FOREIGN KEY ("site_id") REFERENCES "cms_site" ("id") ON DELETE CASCADE
);

CREATE INDEX "IX_cms_plugin_site_site_id" ON "cms_plugin_site" ("site_id");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261107024804_AddPluginSites', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE INDEX "ix_cms_document_trash" ON "cms_document" ("type", "deleted_at") WHERE deleted_at IS NOT NULL;

CREATE INDEX "ix_cms_document_type_site" ON "cms_document" ("type", "site_id", "deleted_at");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261107180158_AddDocumentIndexes', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "cms_api_key" ADD "allowed_addresses" TEXT NULL;

ALTER TABLE "cms_api_key" ADD "previous_key_expires_at" INTEGER NULL;

ALTER TABLE "cms_api_key" ADD "previous_key_hash" TEXT NULL;

ALTER TABLE "cms_api_key" ADD "previous_key_prefix" TEXT NULL;

CREATE INDEX "ix_cms_api_key_previous_key_prefix" ON "cms_api_key" ("previous_key_prefix");

CREATE TABLE "ef_temp_cms_api_key" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_api_key" PRIMARY KEY,
    "allowed_addresses" TEXT NULL,
    "class" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NOT NULL,
    "expires_at" INTEGER NULL,
    "key_hash" TEXT NOT NULL,
    "key_prefix" TEXT NOT NULL,
    "last_used_at" INTEGER NULL,
    "name" TEXT NOT NULL,
    "previous_key_expires_at" INTEGER NULL,
    "previous_key_hash" TEXT NULL,
    "previous_key_prefix" TEXT NULL,
    "rate_limit" INTEGER NULL,
    "revoked_at" INTEGER NULL,
    "role_id" TEXT NOT NULL,
    "site_ids" TEXT NULL,
    "type_names" TEXT NULL,
    CONSTRAINT "ck_cms_api_key_allowed_addresses_json" CHECK (json_valid("allowed_addresses")),
    CONSTRAINT "ck_cms_api_key_site_ids_json" CHECK (json_valid("site_ids")),
    CONSTRAINT "ck_cms_api_key_type_names_json" CHECK (json_valid("type_names")),
    CONSTRAINT "FK_cms_api_key_cms_role_role_id" FOREIGN KEY ("role_id") REFERENCES "cms_role" ("id") ON DELETE CASCADE
);

INSERT INTO "ef_temp_cms_api_key" ("id", "allowed_addresses", "class", "created_at", "created_by", "expires_at", "key_hash", "key_prefix", "last_used_at", "name", "previous_key_expires_at", "previous_key_hash", "previous_key_prefix", "rate_limit", "revoked_at", "role_id", "site_ids", "type_names")
SELECT "id", "allowed_addresses", "class", "created_at", "created_by", "expires_at", "key_hash", "key_prefix", "last_used_at", "name", "previous_key_expires_at", "previous_key_hash", "previous_key_prefix", "rate_limit", "revoked_at", "role_id", "site_ids", "type_names"
FROM "cms_api_key";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "cms_api_key";

ALTER TABLE "ef_temp_cms_api_key" RENAME TO "cms_api_key";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "ix_cms_api_key_key_prefix" ON "cms_api_key" ("key_prefix");

CREATE INDEX "ix_cms_api_key_previous_key_prefix" ON "cms_api_key" ("previous_key_prefix");

CREATE INDEX "ix_cms_api_key_role" ON "cms_api_key" ("role_id");

CREATE UNIQUE INDEX "uq_cms_api_key_key_hash" ON "cms_api_key" ("key_hash");

COMMIT;

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261110033310_AddApiKeyAddressesAndRotation', '10.0.14');

BEGIN TRANSACTION;
CREATE TABLE "cms_idempotency_key" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_idempotency_key" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "principal_kind" TEXT NOT NULL,
    "principal_id" TEXT NOT NULL,
    "key_hash" TEXT NOT NULL,
    "request_hash" TEXT NOT NULL,
    "owner" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL,
    "lease_until" INTEGER NOT NULL,
    "expires_at" INTEGER NOT NULL,
    "response_status" INTEGER NULL,
    "response_headers" TEXT NULL,
    "response_body" TEXT NULL
);

CREATE INDEX "ix_cms_idempotency_key_expires_at" ON "cms_idempotency_key" ("expires_at");

CREATE UNIQUE INDEX "uq_cms_idempotency_key_scope" ON "cms_idempotency_key" ("site_id", "principal_kind", "principal_id", "key_hash");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261110041334_AddIdempotencyKeys', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_member" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_member" PRIMARY KEY,
    "display_name" TEXT NULL,
    "created_at" INTEGER NOT NULL,
    "deactivated_at" INTEGER NULL,
    "last_login_at" INTEGER NULL,
    "email" TEXT NOT NULL,
    "normalized_email" TEXT NOT NULL,
    "email_confirmed" INTEGER NOT NULL,
    "password_hash" TEXT NULL,
    "security_stamp" TEXT NULL,
    "concurrency_stamp" TEXT NULL,
    "lockout_end" INTEGER NULL,
    "access_failed_count" INTEGER NOT NULL
);

CREATE TABLE "cms_member_group" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_member_group" PRIMARY KEY,
    "name" TEXT NOT NULL,
    "description" TEXT NULL,
    "role_id" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL,
    CONSTRAINT "FK_cms_member_group_cms_role_role_id" FOREIGN KEY ("role_id") REFERENCES "cms_role" ("id") ON DELETE CASCADE
);

CREATE TABLE "cms_member_login" (
    "login_provider" TEXT NOT NULL,
    "provider_key" TEXT NOT NULL,
    "provider_display_name" TEXT NULL,
    "member_id" TEXT NOT NULL,
    CONSTRAINT "PK_cms_member_login" PRIMARY KEY ("login_provider", "provider_key"),
    CONSTRAINT "FK_cms_member_login_cms_member_member_id" FOREIGN KEY ("member_id") REFERENCES "cms_member" ("id") ON DELETE CASCADE
);

CREATE TABLE "cms_member_token" (
    "member_id" TEXT NOT NULL,
    "login_provider" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "value" TEXT NULL,
    CONSTRAINT "PK_cms_member_token" PRIMARY KEY ("member_id", "login_provider", "name"),
    CONSTRAINT "FK_cms_member_token_cms_member_member_id" FOREIGN KEY ("member_id") REFERENCES "cms_member" ("id") ON DELETE CASCADE
);

CREATE TABLE "cms_member_group_member" (
    "group_id" TEXT NOT NULL,
    "member_id" TEXT NOT NULL,
    CONSTRAINT "PK_cms_member_group_member" PRIMARY KEY ("group_id", "member_id"),
    CONSTRAINT "FK_cms_member_group_member_cms_member_group_group_id" FOREIGN KEY ("group_id") REFERENCES "cms_member_group" ("id") ON DELETE CASCADE,
    CONSTRAINT "FK_cms_member_group_member_cms_member_member_id" FOREIGN KEY ("member_id") REFERENCES "cms_member" ("id") ON DELETE CASCADE
);

CREATE INDEX "ix_cms_member_email_confirmed_created_at" ON "cms_member" ("email_confirmed", "created_at");

CREATE UNIQUE INDEX "uq_cms_member_normalized_email" ON "cms_member" ("normalized_email");

CREATE INDEX "ix_cms_member_group_role" ON "cms_member_group" ("role_id");

CREATE UNIQUE INDEX "uq_cms_member_group_name" ON "cms_member_group" ("name");

CREATE INDEX "ix_cms_member_group_member_member" ON "cms_member_group_member" ("member_id");

CREATE INDEX "ix_cms_member_login_member" ON "cms_member_login" ("member_id");

INSERT INTO "cms__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261111061643_AddMembers', '10.0.14');

COMMIT;

