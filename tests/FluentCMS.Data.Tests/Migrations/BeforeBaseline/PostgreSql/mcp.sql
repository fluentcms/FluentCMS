-- Test data of BaselineTests, not shipped: what the migrations of stream mcp ran on PostgreSQL before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
CREATE TABLE IF NOT EXISTS cms_mcp__migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_cms_mcp__migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE cms_mcp_instruction (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    type character varying(64),
    text text NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    CONSTRAINT "PK_cms_mcp_instruction" PRIMARY KEY (id)
);

CREATE TABLE cms_mcp_prompt (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    name character varying(64) NOT NULL,
    title character varying(100),
    description character varying(500),
    arguments text NOT NULL,
    template text NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    CONSTRAINT "PK_cms_mcp_prompt" PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ux_cms_mcp_instruction_site ON cms_mcp_instruction (site_id) WHERE "type" IS NULL;

CREATE UNIQUE INDEX ux_cms_mcp_instruction_site_type ON cms_mcp_instruction (site_id, type) WHERE "type" IS NOT NULL;

CREATE UNIQUE INDEX ux_cms_mcp_prompt_site_name ON cms_mcp_prompt (site_id, name);

INSERT INTO cms_mcp__migrations ("MigrationId", "ProductVersion")
VALUES ('20261110142538_InitialMcp', '10.0.14');

COMMIT;

