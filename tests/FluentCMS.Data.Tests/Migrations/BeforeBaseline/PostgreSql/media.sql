-- Test data of BaselineTests, not shipped: what the migrations of stream media ran on PostgreSQL before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS cms_media__migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_cms_media__migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE cms_media_asset (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    folder_id uuid,
    file_name character varying(255) NOT NULL,
    storage_provider character varying(64) NOT NULL,
    storage_key character varying(512) NOT NULL,
    mime_type character varying(128) NOT NULL,
    size bigint NOT NULL,
    checksum character varying(64) NOT NULL,
    width integer,
    height integer,
    duration double precision,
    title character varying(256),
    alt text NOT NULL,
    caption text NOT NULL,
    credit character varying(256),
    tags text NOT NULL,
    focal_x double precision,
    focal_y double precision,
    is_private boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT "PK_cms_media_asset" PRIMARY KEY (id)
);

CREATE TABLE cms_media_folder (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    parent_id uuid,
    name character varying(128) NOT NULL,
    path character varying(1024) NOT NULL,
    CONSTRAINT "PK_cms_media_folder" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_media_asset_site_created_at ON cms_media_asset (site_id, created_at);

CREATE INDEX ix_cms_media_asset_site_folder ON cms_media_asset (site_id, folder_id);

CREATE INDEX ix_cms_media_folder_site_path ON cms_media_folder (site_id, path);

CREATE UNIQUE INDEX ux_cms_media_folder_site_parent_name ON cms_media_folder (site_id, parent_id, name);

INSERT INTO cms_media__migrations ("MigrationId", "ProductVersion")
VALUES ('20261108191452_InitialMedia', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_media_preset (
    site_id uuid NOT NULL,
    name character varying(64) NOT NULL,
    params text NOT NULL,
    CONSTRAINT "PK_cms_media_preset" PRIMARY KEY (site_id, name)
);

CREATE TABLE cms_media_signing_key (
    id integer NOT NULL,
    key text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_media_signing_key" PRIMARY KEY (id)
);

CREATE TABLE cms_media_variant (
    asset_id uuid NOT NULL,
    params_hash character varying(64) NOT NULL,
    storage_key character varying(512) NOT NULL,
    size bigint NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_media_variant" PRIMARY KEY (asset_id, params_hash)
);

INSERT INTO cms_media__migrations ("MigrationId", "ProductVersion")
VALUES ('20261108191503_MediaTransforms', '10.0.14');

COMMIT;

