-- Test data of BaselineTests, not shipped: what the migrations of stream multi-site ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
CREATE TABLE IF NOT EXISTS "cms_multi_site__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms_multi_site__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_multi_site_host" (
    "hostname" TEXT NOT NULL CONSTRAINT "PK_cms_multi_site_host" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "is_primary" INTEGER NOT NULL,
    "created_at" INTEGER NOT NULL
);

CREATE INDEX "ix_cms_multi_site_host_site" ON "cms_multi_site_host" ("site_id");

INSERT INTO "cms_multi_site__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261111042208_InitialMultiSite', '10.0.14');

COMMIT;

