-- Test data of BaselineTests, not shipped: what the migrations of stream search ran on SQLite before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS "cms_search__migrations" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_cms_search__migrations" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "cms_search_entry" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_cms_search_entry" PRIMARY KEY AUTOINCREMENT,
    "document_id" TEXT NOT NULL,
    "locale" TEXT NOT NULL,
    "state" INTEGER NOT NULL,
    "type" TEXT NOT NULL,
    "site_id" TEXT NULL,
    "text" TEXT NOT NULL,
    "updated_at" INTEGER NOT NULL
);

CREATE INDEX "ix_cms_search_entry_site" ON "cms_search_entry" ("site_id");

CREATE INDEX "ix_cms_search_entry_type" ON "cms_search_entry" ("type", "locale", "state", "site_id");

CREATE UNIQUE INDEX "ux_cms_search_entry_document" ON "cms_search_entry" ("document_id", "locale", "state");

CREATE VIRTUAL TABLE cms_search_entry_fts USING fts5("text", content='cms_search_entry', content_rowid='id');

CREATE TRIGGER cms_search_entry_fts_insert AFTER INSERT ON cms_search_entry BEGIN
    INSERT INTO cms_search_entry_fts(rowid, "text") VALUES (new.id, new."text");
END;

CREATE TRIGGER cms_search_entry_fts_delete AFTER DELETE ON cms_search_entry BEGIN
    INSERT INTO cms_search_entry_fts(cms_search_entry_fts, rowid, "text") VALUES ('delete', old.id, old."text");
END;

CREATE TRIGGER cms_search_entry_fts_update AFTER UPDATE OF "text" ON cms_search_entry BEGIN
    INSERT INTO cms_search_entry_fts(cms_search_entry_fts, rowid, "text") VALUES ('delete', old.id, old."text");
    INSERT INTO cms_search_entry_fts(rowid, "text") VALUES (new.id, new."text");
END;

INSERT INTO "cms_search__migrations" ("MigrationId", "ProductVersion")
VALUES ('20261113005122_InitialSearch', '10.0.14');

COMMIT;

