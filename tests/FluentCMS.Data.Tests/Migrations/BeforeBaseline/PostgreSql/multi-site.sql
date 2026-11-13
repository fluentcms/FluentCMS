-- Test data of BaselineTests, not shipped: what the migrations of stream multi-site ran on PostgreSQL before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS cms_multi_site__migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_cms_multi_site__migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE cms_multi_site_host (
    hostname character varying(253) NOT NULL,
    site_id uuid NOT NULL,
    is_primary boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_multi_site_host" PRIMARY KEY (hostname)
);

CREATE INDEX ix_cms_multi_site_host_site ON cms_multi_site_host (site_id);

INSERT INTO cms_multi_site__migrations ("MigrationId", "ProductVersion")
VALUES ('20261111042224_InitialMultiSite', '10.0.14');

COMMIT;

