-- Test data of BaselineTests, not shipped: what the migrations of stream media ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS "cms_media__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms_media__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_media_asset" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_media_asset" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "folder_id" TEXT NULL,
    "file_name" TEXT NOT NULL,
    "storage_provider" TEXT NOT NULL,
    "storage_key" TEXT NOT NULL,
    "mime_type" TEXT NOT NULL,
    "size" INTEGER NOT NULL,
    "checksum" TEXT NOT NULL,
    "width" INTEGER NULL,
    "height" INTEGER NULL,
    "duration" REAL NULL,
    "title" TEXT NULL,
    "alt" TEXT NOT NULL,
    "caption" TEXT NOT NULL,
    "credit" TEXT NULL,
    "tags" TEXT NOT NULL,
    "focal_x" REAL NULL,
    "focal_y" REAL NULL,
    "is_private" INTEGER NOT NULL,
    "created_at" INTEGER NOT NULL,
    "created_by" TEXT NOT NULL,
    "updated_at" INTEGER NOT NULL,
    "updated_by" TEXT NOT NULL,
    "deleted_at" INTEGER NULL
);

CREATE TABLE "cms_media_folder" (
    "id" TEXT NOT NULL CONSTRAINT "PK_cms_media_folder" PRIMARY KEY,
    "site_id" TEXT NOT NULL,
    "parent_id" TEXT NULL,
    "name" TEXT NOT NULL,
    "path" TEXT NOT NULL
);

CREATE INDEX "ix_cms_media_asset_site_created_at" ON "cms_media_asset" ("site_id", "created_at");

CREATE INDEX "ix_cms_media_asset_site_folder" ON "cms_media_asset" ("site_id", "folder_id");

CREATE INDEX "ix_cms_media_folder_site_path" ON "cms_media_folder" ("site_id", "path");

CREATE UNIQUE INDEX "ux_cms_media_folder_site_parent_name" ON "cms_media_folder" ("site_id", "parent_id", "name");

INSERT INTO "cms_media__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261108135721_InitialMedia', '10.0.14');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "cms_media_preset" (
    "site_id" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "params" TEXT NOT NULL,
    CONSTRAINT "PK_cms_media_preset" PRIMARY KEY ("site_id", "name")
);

CREATE TABLE "cms_media_signing_key" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_cms_media_signing_key" PRIMARY KEY,
    "key" TEXT NOT NULL,
    "created_at" INTEGER NOT NULL
);

CREATE TABLE "cms_media_variant" (
    "asset_id" TEXT NOT NULL,
    "params_hash" TEXT NOT NULL,
    "storage_key" TEXT NOT NULL,
    "size" INTEGER NOT NULL,
    "created_at" INTEGER NOT NULL,
    CONSTRAINT "PK_cms_media_variant" PRIMARY KEY ("asset_id", "params_hash")
);

INSERT INTO "cms_media__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261108145445_MediaTransforms', '10.0.14');

COMMIT;

