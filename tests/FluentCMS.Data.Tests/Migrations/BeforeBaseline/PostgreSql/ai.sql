-- Test data of BaselineTests, not shipped: what the migrations of stream ai ran on PostgreSQL before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
CREATE TABLE IF NOT EXISTS cms_ai__migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_cms_ai__migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE cms_ai_usage (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    principal_kind character varying(32) NOT NULL,
    principal_id uuid NOT NULL,
    action character varying(32) NOT NULL,
    type character varying(64) NOT NULL,
    field character varying(64) NOT NULL,
    provider character varying(64) NOT NULL,
    model character varying(128) NOT NULL,
    prompt_tokens bigint NOT NULL,
    completion_tokens bigint NOT NULL,
    duration_ms bigint NOT NULL,
    succeeded boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_ai_usage" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_ai_usage_created_at ON cms_ai_usage (created_at);

CREATE INDEX ix_cms_ai_usage_site_created_at ON cms_ai_usage (site_id, created_at);

INSERT INTO cms_ai__migrations ("MigrationId", "ProductVersion")
VALUES ('20261111053842_InitialAi', '10.0.14');

COMMIT;

