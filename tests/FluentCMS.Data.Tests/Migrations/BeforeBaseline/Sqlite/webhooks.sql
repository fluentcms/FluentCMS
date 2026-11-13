-- Test data of BaselineTests, not shipped: what the migrations of stream webhooks ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS "cms_webhooks__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms_webhooks__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_webhooks_delivery" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_webhooks_delivery" PRIMARY KEY,
    "delivery_id" TEXT NOT NULL,
    "webhook_id" TEXT NOT NULL,
    "event_id" TEXT NOT NULL,
    "event" TEXT NOT NULL,
    "attempt" INTEGER NOT NULL,
    "state" TEXT NOT NULL,
    "status_code" INTEGER NULL,
    "duration_ms" INTEGER NOT NULL,
    "request_body" TEXT NOT NULL,
    "response_excerpt" TEXT NULL,
    "error" TEXT NULL,
    "next_retry_at" INTEGER NULL,
    "created_at" INTEGER NOT NULL
);

CREATE TABLE "cms_webhooks_subscription" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_webhooks_subscription" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "url" TEXT NOT NULL,
    "secret" TEXT NOT NULL,
    "events" TEXT NOT NULL,
    "types" TEXT NOT NULL,
    "headers" TEXT NOT NULL,
    "enabled" INTEGER NOT NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NOT NULL,
    "updated_at" INTEGER NOT NULL,
    "updated_by" TEXT NOT NULL
);

CREATE INDEX "ix_cms_webhooks_delivery_created_at" ON "cms_webhooks_delivery" ("created_at");

CREATE INDEX "ix_cms_webhooks_delivery_webhook" ON "cms_webhooks_delivery" ("webhook_id", "created_at");

CREATE UNIQUE INDEX "ux_cms_webhooks_delivery_attempt" ON "cms_webhooks_delivery" ("delivery_id", "attempt");

CREATE UNIQUE INDEX "ux_cms_webhooks_subscription_site_name" ON "cms_webhooks_subscription" ("site_id", "name");

INSERT INTO "cms_webhooks__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261107030021_InitialWebhooks', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "cms_webhooks_delivery" ADD "manual" INTEGER NOT NULL DEFAULT 0;

CREATE UNIQUE INDEX "ux_cms_webhooks_delivery_in_flight" ON "cms_webhooks_delivery" ("webhook_id") WHERE state = 'pending';

INSERT INTO "cms_webhooks__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261107152858_AddDeliveryClaim', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "cms_webhooks_subscription" ADD "previous_secret" TEXT NULL;

ALTER TABLE "cms_webhooks_subscription" ADD "previous_secret_expires_at" INTEGER NULL;

INSERT INTO "cms_webhooks__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261107173344_AddPreviousSecret', '10.0.14');

COMMIT;

