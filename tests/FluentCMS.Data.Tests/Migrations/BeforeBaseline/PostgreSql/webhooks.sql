-- Test data of BaselineTests, not shipped: what the migrations of stream webhooks ran on PostgreSQL before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit f211dd92.
CREATE TABLE IF NOT EXISTS cms_webhooks__migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_cms_webhooks__migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE cms_webhooks_delivery (
    id uuid NOT NULL,
    delivery_id uuid NOT NULL,
    webhook_id uuid NOT NULL,
    event_id uuid NOT NULL,
    event character varying(64) NOT NULL,
    attempt integer NOT NULL,
    state character varying(16) NOT NULL,
    status_code integer,
    duration_ms bigint NOT NULL,
    request_body text NOT NULL,
    response_excerpt character varying(1024),
    error character varying(1000),
    next_retry_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_webhooks_delivery" PRIMARY KEY (id)
);

CREATE TABLE cms_webhooks_subscription (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    url character varying(2048) NOT NULL,
    secret text NOT NULL,
    events text NOT NULL,
    types text NOT NULL,
    headers text NOT NULL,
    enabled boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    CONSTRAINT "PK_cms_webhooks_subscription" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_webhooks_delivery_created_at ON cms_webhooks_delivery (created_at);

CREATE INDEX ix_cms_webhooks_delivery_webhook ON cms_webhooks_delivery (webhook_id, created_at);

CREATE UNIQUE INDEX ux_cms_webhooks_delivery_attempt ON cms_webhooks_delivery (delivery_id, attempt);

CREATE UNIQUE INDEX ux_cms_webhooks_subscription_site_name ON cms_webhooks_subscription (site_id, name);

INSERT INTO cms_webhooks__migrations ("MigrationId", "ProductVersion")
VALUES ('20261108191320_InitialWebhooks', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE cms_webhooks_delivery ADD manual boolean NOT NULL DEFAULT FALSE;

CREATE UNIQUE INDEX ux_cms_webhooks_delivery_in_flight ON cms_webhooks_delivery (webhook_id) WHERE state = 'pending';

INSERT INTO cms_webhooks__migrations ("MigrationId", "ProductVersion")
VALUES ('20261108191332_AddDeliveryClaim', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE cms_webhooks_subscription ADD previous_secret text;

ALTER TABLE cms_webhooks_subscription ADD previous_secret_expires_at timestamp with time zone;

INSERT INTO cms_webhooks__migrations ("MigrationId", "ProductVersion")
VALUES ('20261108191343_AddPreviousSecret', '10.0.14');

COMMIT;

