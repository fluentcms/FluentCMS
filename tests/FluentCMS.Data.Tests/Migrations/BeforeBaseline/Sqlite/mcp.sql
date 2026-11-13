-- Test data of BaselineTests, not shipped: what the migrations of stream mcp ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS "cms_mcp__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms_mcp__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_mcp_instruction" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_mcp_instruction" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "type" TEXT NULL,
    "text" TEXT NOT NULL,
    "updated_at" INTEGER NOT NULL,
    "updated_by" TEXT NOT NULL
);

CREATE TABLE "cms_mcp_prompt" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_mcp_prompt" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "title" TEXT NULL,
    "description" TEXT NULL,
    "arguments" TEXT NOT NULL,
    "template" TEXT NOT NULL,
    "updated_at" INTEGER NOT NULL,
    "updated_by" TEXT NOT NULL
);

CREATE UNIQUE INDEX "ux_cms_mcp_instruction_site" ON "cms_mcp_instruction" ("site_id") WHERE "type" IS NULL;

CREATE UNIQUE INDEX "ux_cms_mcp_instruction_site_type" ON "cms_mcp_instruction" ("site_id", "type") WHERE "type" IS NOT NULL;

CREATE UNIQUE INDEX "ux_cms_mcp_prompt_site_name" ON "cms_mcp_prompt" ("site_id", "name");

INSERT INTO "cms_mcp__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261110142423_InitialMcp', '10.0.14');

COMMIT;

