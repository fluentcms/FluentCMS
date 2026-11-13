-- Test data of BaselineTests, not shipped: what the migrations of stream ai ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
CREATE TABLE IF NOT EXISTS "cms_ai__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms_ai__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_ai_usage" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_ai_usage" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "principal_kind" TEXT NOT NULL,
    "principal_id" TEXT NOT NULL,
    "action" TEXT NOT NULL,
    "type" TEXT NOT NULL,
    "field" TEXT NOT NULL,
    "provider" TEXT NOT NULL,
    "model" TEXT NOT NULL,
    "prompt_tokens" INTEGER NOT NULL,
    "completion_tokens" INTEGER NOT NULL,
    "duration_ms" INTEGER NOT NULL,
    "succeeded" INTEGER NOT NULL,
    "created_at" INTEGER NOT NULL
);

CREATE INDEX "ix_cms_ai_usage_created_at" ON "cms_ai_usage" ("created_at");

CREATE INDEX "ix_cms_ai_usage_site_created_at" ON "cms_ai_usage" ("site_id", "created_at");

INSERT INTO "cms_ai__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261111053827_InitialAi', '10.0.14');

COMMIT;

