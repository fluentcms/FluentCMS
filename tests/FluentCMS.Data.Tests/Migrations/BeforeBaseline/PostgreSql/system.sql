-- Test data of BaselineTests, not shipped: what the migrations of stream system ran on PostgreSQL before the baseline
-- of #3052, as 'dotnet ef migrations script' wrote them at commit 8e8dbe05.
CREATE TABLE IF NOT EXISTS cms__migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_cms__migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE cms_document (
    id uuid NOT NULL,
    document_id uuid NOT NULL,
    locale character varying(16) NOT NULL,
    type character varying(64) NOT NULL,
    site_id uuid,
    status character varying(16) NOT NULL,
    draft_version integer NOT NULL,
    published_version integer,
    published_at timestamp with time zone,
    first_published_at timestamp with time zone,
    deleted_at timestamp with time zone,
    deleted_by uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    schedule_publish_at timestamp with time zone,
    schedule_unpublish_at timestamp with time zone,
    CONSTRAINT "PK_cms_document" PRIMARY KEY (id)
);

CREATE TABLE cms_reference (
    id uuid NOT NULL,
    source_type character varying(64) NOT NULL,
    source_document_id uuid NOT NULL,
    state smallint NOT NULL,
    locale character varying(16) NOT NULL,
    field character varying(64) NOT NULL,
    position integer NOT NULL,
    target_type character varying(64) NOT NULL,
    target_id uuid NOT NULL,
    CONSTRAINT "PK_cms_reference" PRIMARY KEY (id)
);

CREATE TABLE cms_version (
    id uuid NOT NULL,
    document_id uuid NOT NULL,
    locale character varying(16) NOT NULL,
    number integer NOT NULL,
    snapshot jsonb NOT NULL,
    status_after character varying(16) NOT NULL,
    author_id uuid NOT NULL,
    origin character varying(16) NOT NULL,
    client_id text,
    message text,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_version" PRIMARY KEY (id)
);

CREATE UNIQUE INDEX uq_cms_document_document_locale ON cms_document (document_id, locale);

CREATE INDEX ix_cms_reference_target ON cms_reference (target_type, target_id);

CREATE UNIQUE INDEX uq_cms_reference_source ON cms_reference (source_type, source_document_id, state, locale, field, position);

CREATE UNIQUE INDEX uq_cms_version_document_locale_number ON cms_version (document_id, locale, number);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261102151140_InitialSystem', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_role (
    id uuid NOT NULL,
    site_id uuid,
    name character varying(128) NOT NULL,
    description character varying(512),
    built_in boolean NOT NULL,
    permissions_version bigint NOT NULL,
    CONSTRAINT "PK_cms_role" PRIMARY KEY (id)
);

CREATE TABLE cms_role_permission (
    id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission character varying(128) NOT NULL,
    constraints jsonb,
    CONSTRAINT "PK_cms_role_permission" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_role_permission_cms_role_role_id" FOREIGN KEY (role_id) REFERENCES cms_role (id) ON DELETE CASCADE
);

INSERT INTO cms_role (id, built_in, description, name, permissions_version, site_id)
VALUES ('00000000-0000-7000-8000-000000000001', TRUE, 'Holds every permission.', 'Super Admin', 1, NULL);
INSERT INTO cms_role (id, built_in, description, name, permissions_version, site_id)
VALUES ('00000000-0000-7000-8000-000000000002', TRUE, 'Creates, edits, publishes and removes any content.', 'Editor', 1, NULL);
INSERT INTO cms_role (id, built_in, description, name, permissions_version, site_id)
VALUES ('00000000-0000-7000-8000-000000000003', TRUE, 'Creates content, and edits or removes only their own entries.', 'Author', 1, NULL);
INSERT INTO cms_role (id, built_in, description, name, permissions_version, site_id)
VALUES ('00000000-0000-7000-8000-000000000004', TRUE, 'The anonymous visitor. Grants here open public Delivery access.', 'Public', 1, NULL);
INSERT INTO cms_role (id, built_in, description, name, permissions_version, site_id)
VALUES ('00000000-0000-7000-8000-000000000005', TRUE, 'The default role of a registered member.', 'Member', 1, NULL);

INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000001', NULL, '*', '00000000-0000-7000-8000-000000000001');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000002', NULL, 'content.*.create', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000003', NULL, 'content.*.read', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000004', NULL, 'content.*.update', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000005', NULL, 'content.*.delete', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000006', NULL, 'content.*.publish', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000007', NULL, 'content.*.unpublish', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000008', NULL, 'content.*.restore', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000009', NULL, 'schema.read', '00000000-0000-7000-8000-000000000002');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000010', NULL, 'content.*.create', '00000000-0000-7000-8000-000000000003');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000011', NULL, 'content.*.read', '00000000-0000-7000-8000-000000000003');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000012', '{"rowRule":"own-entries"}', 'content.*.update', '00000000-0000-7000-8000-000000000003');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000013', '{"rowRule":"own-entries"}', 'content.*.delete', '00000000-0000-7000-8000-000000000003');
INSERT INTO cms_role_permission (id, constraints, permission, role_id)
VALUES ('00000000-0000-7000-8001-000000000014', NULL, 'schema.read', '00000000-0000-7000-8000-000000000003');

CREATE UNIQUE INDEX uq_cms_role_name ON cms_role (name) WHERE site_id IS NULL;

CREATE UNIQUE INDEX uq_cms_role_site_name ON cms_role (site_id, name) WHERE site_id IS NOT NULL;

CREATE INDEX ix_cms_role_permission_role_permission ON cms_role_permission (role_id, permission);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261102155500_AddRoles', '10.0.14');

COMMIT;

START TRANSACTION;
UPDATE cms_role SET description = 'Creates content, and reads, edits or removes only their own entries.'
WHERE id = '00000000-0000-7000-8000-000000000003';

UPDATE cms_role_permission SET constraints = '{"rowRule":"own-entries"}'
WHERE id = '00000000-0000-7000-8001-000000000011';

UPDATE cms_role SET permissions_version = permissions_version + 1 WHERE id = '00000000-0000-7000-8000-000000000003';

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261102171500_AuthorReadsOwnEntries', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_site (
    id uuid NOT NULL,
    name character varying(128) NOT NULL,
    slug character varying(64) NOT NULL,
    default_locale character varying(16) NOT NULL,
    is_default boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_site" PRIMARY KEY (id)
);

CREATE TABLE cms_site_locale (
    site_id uuid NOT NULL,
    code character varying(16) NOT NULL,
    position integer NOT NULL,
    fallbacks jsonb,
    enabled boolean NOT NULL,
    CONSTRAINT "PK_cms_site_locale" PRIMARY KEY (site_id, code),
    CONSTRAINT "FK_cms_site_locale_cms_site_site_id" FOREIGN KEY (site_id) REFERENCES cms_site (id) ON DELETE CASCADE
);

INSERT INTO cms_site (id, created_at, default_locale, is_default, name, slug)
VALUES ('00000000-0000-7000-8002-000000000001', TIMESTAMPTZ '2026-11-02T00:00:00+00:00', 'en', TRUE, 'Default', 'default');

INSERT INTO cms_role (id, built_in, description, name, permissions_version, site_id)
VALUES ('00000000-0000-7000-8000-000000000006', TRUE, 'The anonymous visitor. Grants here open public Delivery access.', 'Public', 1, '00000000-0000-7000-8002-000000000001');

INSERT INTO cms_site_locale (code, site_id, enabled, fallbacks, position)
VALUES ('en', '00000000-0000-7000-8002-000000000001', TRUE, NULL, 0);

CREATE UNIQUE INDEX uq_cms_site_slug ON cms_site (slug);

ALTER TABLE cms_role ADD CONSTRAINT "FK_cms_role_cms_site_site_id" FOREIGN KEY (site_id) REFERENCES cms_site (id) ON DELETE CASCADE;

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261102181213_AddSites', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_job (
    id uuid NOT NULL,
    type character varying(128) NOT NULL,
    payload jsonb NOT NULL,
    queue character varying(64) NOT NULL,
    site_id uuid,
    run_at timestamp with time zone NOT NULL,
    priority integer NOT NULL,
    status character varying(16) NOT NULL,
    attempts integer NOT NULL,
    max_attempts integer NOT NULL,
    lease_until timestamp with time zone,
    lease_owner character varying(128),
    last_error character varying(2000),
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    origin character varying(16) NOT NULL,
    completed_at timestamp with time zone,
    CONSTRAINT "PK_cms_job" PRIMARY KEY (id)
);

CREATE TABLE cms_recurring_job (
    name character varying(128) NOT NULL,
    cron character varying(128) NOT NULL,
    type character varying(128) NOT NULL,
    payload jsonb NOT NULL,
    queue character varying(64) NOT NULL,
    next_run_at timestamp with time zone NOT NULL,
    enabled boolean NOT NULL,
    CONSTRAINT "PK_cms_recurring_job" PRIMARY KEY (name)
);

CREATE TABLE cms_job_attempt (
    job_id uuid NOT NULL,
    attempt integer NOT NULL,
    started_at timestamp with time zone NOT NULL,
    finished_at timestamp with time zone,
    outcome character varying(16),
    error character varying(2000),
    CONSTRAINT "PK_cms_job_attempt" PRIMARY KEY (job_id, attempt),
    CONSTRAINT "FK_cms_job_attempt_cms_job_job_id" FOREIGN KEY (job_id) REFERENCES cms_job (id) ON DELETE CASCADE
);

CREATE INDEX ix_cms_job_claim ON cms_job (queue, priority DESC, run_at, id) WHERE status IN ('pending', 'running');

CREATE INDEX ix_cms_job_status_lease ON cms_job (status, lease_until);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261102202439_AddJobs', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_outbox (
    id uuid NOT NULL,
    event_id uuid NOT NULL,
    event_type character varying(256) NOT NULL,
    handler character varying(128) NOT NULL,
    payload jsonb NOT NULL,
    site_id uuid,
    occurred_at timestamp with time zone NOT NULL,
    sequence bigint NOT NULL,
    status character varying(16) NOT NULL,
    attempts integer NOT NULL,
    available_at timestamp with time zone NOT NULL,
    lease_until timestamp with time zone,
    lease_owner character varying(128),
    last_error character varying(2000),
    delivered_at timestamp with time zone,
    CONSTRAINT "PK_cms_outbox" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_outbox_delivered ON cms_outbox (delivered_at) WHERE status = 'delivered';

CREATE INDEX ix_cms_outbox_pending ON cms_outbox (sequence, id) WHERE status = 'pending';

CREATE UNIQUE INDEX uq_cms_outbox_event_handler ON cms_outbox (event_id, handler);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261102231104_AddOutbox', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_activity (
    id uuid NOT NULL,
    site_id uuid,
    occurred_at timestamp with time zone NOT NULL,
    actor_type character varying(16) NOT NULL,
    actor_id uuid NOT NULL,
    actor_name character varying(128) NOT NULL,
    origin character varying(16) NOT NULL,
    client_id character varying(128),
    action character varying(64) NOT NULL,
    target_type character varying(64),
    target_id character varying(128),
    target_label character varying(256),
    locale character varying(16),
    details jsonb,
    correlation_id character varying(128) NOT NULL,
    ip character varying(45),
    CONSTRAINT "PK_cms_activity" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_activity_actor ON cms_activity (actor_id);

CREATE INDEX ix_cms_activity_occurred ON cms_activity (occurred_at);

CREATE INDEX ix_cms_activity_site_occurred ON cms_activity (site_id, occurred_at);

CREATE INDEX ix_cms_activity_target ON cms_activity (target_type, target_id);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261103135357_AddActivity', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_schedule (
    id uuid NOT NULL,
    document_id uuid NOT NULL,
    locale character varying(16) NOT NULL,
    action character varying(16) NOT NULL,
    run_at_utc timestamp with time zone NOT NULL,
    time_zone character varying(64),
    status character varying(16) NOT NULL,
    job_id uuid,
    version integer NOT NULL,
    shared_hash character varying(64),
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    claims jsonb,
    completed_at timestamp with time zone,
    error character varying(2000),
    CONSTRAINT "PK_cms_schedule" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_schedule_document ON cms_schedule (document_id);

CREATE UNIQUE INDEX uq_cms_schedule_pending ON cms_schedule (document_id, locale, action) WHERE status = 'pending';

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261104001146_AddSchedule', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_setting (
    id uuid NOT NULL,
    site_id uuid,
    key character varying(64) NOT NULL,
    json jsonb NOT NULL,
    version integer NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    CONSTRAINT "PK_cms_setting" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_setting_cms_site_site_id" FOREIGN KEY (site_id) REFERENCES cms_site (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX uq_cms_setting_instance_key ON cms_setting (key) WHERE site_id IS NULL;

CREATE UNIQUE INDEX uq_cms_setting_site_key ON cms_setting (site_id, key) WHERE site_id IS NOT NULL;

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261104034356_AddSettings', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_schema_history (
    id bigint GENERATED BY DEFAULT AS IDENTITY,
    hash character varying(64) NOT NULL,
    snapshot text NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    fluentcms_version character varying(64) NOT NULL,
    CONSTRAINT "PK_cms_schema_history" PRIMARY KEY (id)
);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261104135438_AddSchemaHistory', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_invitation (
    id uuid NOT NULL,
    email character varying(256) NOT NULL,
    normalized_email character varying(256) NOT NULL,
    token_hash character varying(64) NOT NULL,
    role_ids jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    created_by_kind character varying(16) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    user_id uuid,
    revoked_at timestamp with time zone,
    CONSTRAINT "PK_cms_invitation" PRIMARY KEY (id)
);

CREATE TABLE cms_user (
    id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    deactivated_at timestamp with time zone,
    must_change_password boolean NOT NULL,
    user_name character varying(256),
    normalized_user_name character varying(256),
    email character varying(256),
    normalized_email character varying(256),
    email_confirmed boolean NOT NULL,
    password_hash text,
    security_stamp character varying(64),
    concurrency_stamp character varying(64),
    phone_number character varying(32),
    phone_number_confirmed boolean NOT NULL,
    two_factor_enabled boolean NOT NULL,
    lockout_end timestamp with time zone,
    lockout_enabled boolean NOT NULL,
    access_failed_count integer NOT NULL,
    CONSTRAINT "PK_cms_user" PRIMARY KEY (id)
);

CREATE TABLE cms_user_claim (
    id integer GENERATED BY DEFAULT AS IDENTITY,
    user_id uuid NOT NULL,
    claim_type character varying(256),
    claim_value text,
    CONSTRAINT "PK_cms_user_claim" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_user_claim_cms_user_user_id" FOREIGN KEY (user_id) REFERENCES cms_user (id) ON DELETE CASCADE
);

CREATE TABLE cms_user_login (
    login_provider character varying(128) NOT NULL,
    provider_key character varying(128) NOT NULL,
    provider_display_name character varying(128),
    user_id uuid NOT NULL,
    CONSTRAINT "PK_cms_user_login" PRIMARY KEY (login_provider, provider_key),
    CONSTRAINT "FK_cms_user_login_cms_user_user_id" FOREIGN KEY (user_id) REFERENCES cms_user (id) ON DELETE CASCADE
);

CREATE TABLE cms_user_role (
    user_id uuid NOT NULL,
    role_id uuid NOT NULL,
    CONSTRAINT "PK_cms_user_role" PRIMARY KEY (user_id, role_id),
    CONSTRAINT "FK_cms_user_role_cms_role_role_id" FOREIGN KEY (role_id) REFERENCES cms_role (id) ON DELETE CASCADE,
    CONSTRAINT "FK_cms_user_role_cms_user_user_id" FOREIGN KEY (user_id) REFERENCES cms_user (id) ON DELETE CASCADE
);

CREATE TABLE cms_user_token (
    user_id uuid NOT NULL,
    login_provider character varying(128) NOT NULL,
    name character varying(128) NOT NULL,
    value text,
    CONSTRAINT "PK_cms_user_token" PRIMARY KEY (user_id, login_provider, name),
    CONSTRAINT "FK_cms_user_token_cms_user_user_id" FOREIGN KEY (user_id) REFERENCES cms_user (id) ON DELETE CASCADE
);

CREATE INDEX ix_cms_invitation_normalized_email ON cms_invitation (normalized_email);

CREATE UNIQUE INDEX uq_cms_invitation_token_hash ON cms_invitation (token_hash);

CREATE UNIQUE INDEX uq_cms_user_normalized_email ON cms_user (normalized_email) WHERE normalized_email IS NOT NULL;

CREATE UNIQUE INDEX uq_cms_user_normalized_user_name ON cms_user (normalized_user_name);

CREATE INDEX ix_cms_user_claim_user ON cms_user_claim (user_id);

CREATE INDEX ix_cms_user_login_user ON cms_user_login (user_id);

CREATE INDEX ix_cms_user_role_role ON cms_user_role (role_id);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261105171335_AddUsers', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_api_key (
    id uuid NOT NULL,
    name character varying(128) NOT NULL,
    class character varying(16) NOT NULL,
    key_prefix character varying(16) NOT NULL,
    key_hash character varying(64) NOT NULL,
    role_id uuid NOT NULL,
    site_ids jsonb,
    type_names jsonb,
    expires_at timestamp with time zone,
    last_used_at timestamp with time zone,
    rate_limit integer,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    CONSTRAINT "PK_cms_api_key" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_api_key_cms_role_role_id" FOREIGN KEY (role_id) REFERENCES cms_role (id) ON DELETE CASCADE
);

CREATE INDEX ix_cms_api_key_key_prefix ON cms_api_key (key_prefix);

CREATE INDEX ix_cms_api_key_role ON cms_api_key (role_id);

CREATE UNIQUE INDEX uq_cms_api_key_key_hash ON cms_api_key (key_hash);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261105181539_AddApiKeys', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_data_protection_key (
    id uuid NOT NULL,
    friendly_name character varying(256),
    xml text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_data_protection_key" PRIMARY KEY (id)
);

CREATE TABLE cms_oidc_application (
    id uuid NOT NULL,
    application_type character varying(50),
    client_id character varying(100),
    client_secret text,
    client_type character varying(50),
    concurrency_token character varying(50),
    consent_type character varying(50),
    display_name text,
    display_names text,
    json_web_key_set text,
    permissions text,
    post_logout_redirect_uris text,
    properties text,
    redirect_uris text,
    requirements text,
    settings text,
    CONSTRAINT "PK_cms_oidc_application" PRIMARY KEY (id)
);

CREATE TABLE cms_oidc_scope (
    id uuid NOT NULL,
    concurrency_token character varying(50),
    description text,
    descriptions text,
    display_name text,
    display_names text,
    name character varying(200),
    properties text,
    resources text,
    CONSTRAINT "PK_cms_oidc_scope" PRIMARY KEY (id)
);

CREATE TABLE cms_signing_key (
    id uuid NOT NULL,
    use character varying(16) NOT NULL,
    algorithm character varying(32) NOT NULL,
    protected_key text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_signing_key" PRIMARY KEY (id)
);

CREATE TABLE cms_oidc_authorization (
    id uuid NOT NULL,
    application_id uuid,
    concurrency_token character varying(50),
    creation_date timestamp with time zone,
    properties text,
    scopes text,
    status character varying(50),
    subject character varying(400),
    type character varying(50),
    CONSTRAINT "PK_cms_oidc_authorization" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_oidc_authorization_cms_oidc_application_application_id" FOREIGN KEY (application_id) REFERENCES cms_oidc_application (id)
);

CREATE TABLE cms_oidc_token (
    id uuid NOT NULL,
    application_id uuid,
    authorization_id uuid,
    concurrency_token character varying(50),
    creation_date timestamp with time zone,
    expiration_date timestamp with time zone,
    payload text,
    properties text,
    redemption_date timestamp with time zone,
    reference_id character varying(100),
    status character varying(50),
    subject character varying(400),
    type character varying(150),
    CONSTRAINT "PK_cms_oidc_token" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_oidc_token_cms_oidc_application_application_id" FOREIGN KEY (application_id) REFERENCES cms_oidc_application (id),
    CONSTRAINT "FK_cms_oidc_token_cms_oidc_authorization_authorization_id" FOREIGN KEY (authorization_id) REFERENCES cms_oidc_authorization (id)
);

CREATE UNIQUE INDEX uq_cms_oidc_application_client_id ON cms_oidc_application (client_id);

CREATE INDEX ix_cms_oidc_authorization_application_id_status_subject_type ON cms_oidc_authorization (application_id, status, subject, type);

CREATE UNIQUE INDEX uq_cms_oidc_scope_name ON cms_oidc_scope (name);

CREATE INDEX ix_cms_oidc_token_application_id_status_subject_type ON cms_oidc_token (application_id, status, subject, type);

CREATE INDEX ix_cms_oidc_token_authorization_id ON cms_oidc_token (authorization_id);

CREATE UNIQUE INDEX uq_cms_oidc_token_reference_id ON cms_oidc_token (reference_id);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261105215443_AddAuthorizationServer', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_lock (
    name character varying(128) NOT NULL,
    owner character varying(128),
    acquired_at timestamp with time zone,
    lease_until timestamp with time zone,
    CONSTRAINT "PK_cms_lock" PRIMARY KEY (name)
);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261106144325_AddLocks', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE cms_role ADD require_two_factor boolean NOT NULL DEFAULT FALSE;

UPDATE cms_role SET require_two_factor = FALSE
WHERE id = '00000000-0000-7000-8000-000000000001';

UPDATE cms_role SET require_two_factor = FALSE
WHERE id = '00000000-0000-7000-8000-000000000002';

UPDATE cms_role SET require_two_factor = FALSE
WHERE id = '00000000-0000-7000-8000-000000000003';

UPDATE cms_role SET require_two_factor = FALSE
WHERE id = '00000000-0000-7000-8000-000000000004';

UPDATE cms_role SET require_two_factor = FALSE
WHERE id = '00000000-0000-7000-8000-000000000005';

UPDATE cms_role SET require_two_factor = FALSE
WHERE id = '00000000-0000-7000-8000-000000000006';

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261106160920_AddRoleTwoFactor', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_plugin_site (
    plugin character varying(32) NOT NULL,
    site_id uuid NOT NULL,
    enabled boolean NOT NULL,
    CONSTRAINT "PK_cms_plugin_site" PRIMARY KEY (plugin, site_id),
    CONSTRAINT "FK_cms_plugin_site_cms_site_site_id" FOREIGN KEY (site_id) REFERENCES cms_site (id) ON DELETE CASCADE
);

CREATE INDEX "IX_cms_plugin_site_site_id" ON cms_plugin_site (site_id);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261107024812_AddPluginSites', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE INDEX ix_cms_document_trash ON cms_document (type, deleted_at) WHERE deleted_at IS NOT NULL;

CREATE INDEX ix_cms_document_type_site ON cms_document (type, site_id, deleted_at);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261107180219_AddDocumentIndexes', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE cms_api_key ADD allowed_addresses jsonb;

ALTER TABLE cms_api_key ADD previous_key_expires_at timestamp with time zone;

ALTER TABLE cms_api_key ADD previous_key_hash character varying(64);

ALTER TABLE cms_api_key ADD previous_key_prefix character varying(16);

CREATE INDEX ix_cms_api_key_previous_key_prefix ON cms_api_key (previous_key_prefix);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261110033325_AddApiKeyAddressesAndRotation', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_idempotency_key (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    principal_kind character varying(16) NOT NULL,
    principal_id uuid NOT NULL,
    key_hash character varying(64) NOT NULL,
    request_hash character varying(64) NOT NULL,
    owner uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    lease_until timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    response_status integer,
    response_headers text,
    response_body text,
    CONSTRAINT "PK_cms_idempotency_key" PRIMARY KEY (id)
);

CREATE INDEX ix_cms_idempotency_key_expires_at ON cms_idempotency_key (expires_at);

CREATE UNIQUE INDEX uq_cms_idempotency_key_scope ON cms_idempotency_key (site_id, principal_kind, principal_id, key_hash);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261110041349_AddIdempotencyKeys', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE cms_member (
    id uuid NOT NULL,
    display_name character varying(128),
    created_at timestamp with time zone NOT NULL,
    deactivated_at timestamp with time zone,
    last_login_at timestamp with time zone,
    email character varying(256) NOT NULL,
    normalized_email character varying(256) NOT NULL,
    email_confirmed boolean NOT NULL,
    password_hash text,
    security_stamp character varying(64),
    concurrency_stamp character varying(64),
    lockout_end timestamp with time zone,
    access_failed_count integer NOT NULL,
    CONSTRAINT "PK_cms_member" PRIMARY KEY (id)
);

CREATE TABLE cms_member_group (
    id uuid NOT NULL,
    name character varying(64) NOT NULL,
    description character varying(512),
    role_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cms_member_group" PRIMARY KEY (id),
    CONSTRAINT "FK_cms_member_group_cms_role_role_id" FOREIGN KEY (role_id) REFERENCES cms_role (id) ON DELETE CASCADE
);

CREATE TABLE cms_member_login (
    login_provider character varying(128) NOT NULL,
    provider_key character varying(128) NOT NULL,
    provider_display_name character varying(128),
    member_id uuid NOT NULL,
    CONSTRAINT "PK_cms_member_login" PRIMARY KEY (login_provider, provider_key),
    CONSTRAINT "FK_cms_member_login_cms_member_member_id" FOREIGN KEY (member_id) REFERENCES cms_member (id) ON DELETE CASCADE
);

CREATE TABLE cms_member_token (
    member_id uuid NOT NULL,
    login_provider character varying(128) NOT NULL,
    name character varying(128) NOT NULL,
    value text,
    CONSTRAINT "PK_cms_member_token" PRIMARY KEY (member_id, login_provider, name),
    CONSTRAINT "FK_cms_member_token_cms_member_member_id" FOREIGN KEY (member_id) REFERENCES cms_member (id) ON DELETE CASCADE
);

CREATE TABLE cms_member_group_member (
    group_id uuid NOT NULL,
    member_id uuid NOT NULL,
    CONSTRAINT "PK_cms_member_group_member" PRIMARY KEY (group_id, member_id),
    CONSTRAINT "FK_cms_member_group_member_cms_member_group_group_id" FOREIGN KEY (group_id) REFERENCES cms_member_group (id) ON DELETE CASCADE,
    CONSTRAINT "FK_cms_member_group_member_cms_member_member_id" FOREIGN KEY (member_id) REFERENCES cms_member (id) ON DELETE CASCADE
);

CREATE INDEX ix_cms_member_email_confirmed_created_at ON cms_member (email_confirmed, created_at);

CREATE UNIQUE INDEX uq_cms_member_normalized_email ON cms_member (normalized_email);

CREATE INDEX ix_cms_member_group_role ON cms_member_group (role_id);

CREATE UNIQUE INDEX uq_cms_member_group_name ON cms_member_group (name);

CREATE INDEX ix_cms_member_group_member_member ON cms_member_group_member (member_id);

CREATE INDEX ix_cms_member_login_member ON cms_member_login (member_id);

INSERT INTO cms__migrations ("MigrationId", "ProductVersion")
VALUES ('20261111061709_AddMembers', '10.0.14');

COMMIT;

