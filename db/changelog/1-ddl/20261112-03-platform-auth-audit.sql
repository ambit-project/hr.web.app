--liquibase formatted sql

--changeset hr:20261112-03
--comment: Rozszerzenia w osobnym schemacie (rutyny mają search_path bez public), konta, role, sesje i audyt.
CREATE SCHEMA ext;
CREATE EXTENSION pgcrypto SCHEMA ext;
CREATE EXTENSION btree_gist SCHEMA ext;
CREATE SCHEMA platform_app;

CREATE TABLE platform.users (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    login text NOT NULL,
    email text NOT NULL,
    display_name text NOT NULL,
    password_hash text NULL,
    is_active boolean NOT NULL DEFAULT true,
    is_dev_account boolean NOT NULL DEFAULT false,
    failed_attempts integer NOT NULL DEFAULT 0,
    locked_until timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT users_login_length CHECK (char_length(login) BETWEEN 3 AND 100),
    CONSTRAINT users_email_format CHECK (email ~ '^[^@\s]+@[^@\s]+$'),
    CONSTRAINT users_display_name_length CHECK (char_length(display_name) BETWEEN 1 AND 200)
);
CREATE UNIQUE INDEX users_login_uq ON platform.users (lower(login));
CREATE UNIQUE INDEX users_email_uq ON platform.users (lower(email));

CREATE TABLE platform.user_roles (
    user_id uuid NOT NULL REFERENCES platform.users (id) ON DELETE CASCADE,
    role text NOT NULL,
    PRIMARY KEY (user_id, role),
    CONSTRAINT user_roles_role CHECK (role IN ('kadry', 'administrator', 'pracownik'))
);

CREATE TABLE platform.sessions (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    user_id uuid NOT NULL REFERENCES platform.users (id) ON DELETE CASCADE,
    token_hash bytea NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX sessions_token_uq ON platform.sessions (token_hash);
CREATE INDEX sessions_user_idx ON platform.sessions (user_id);

CREATE TABLE platform.audit_entries (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at timestamptz NOT NULL DEFAULT now(),
    actor_id uuid NULL,
    entity text NOT NULL,
    entity_id text NOT NULL,
    action text NOT NULL,
    before jsonb NULL,
    after jsonb NULL,
    CONSTRAINT audit_entries_action CHECK (action IN ('create', 'update', 'delete'))
);
CREATE INDEX audit_entries_entity_idx ON platform.audit_entries (entity, entity_id);
CREATE INDEX audit_entries_at_idx ON platform.audit_entries (at);

CREATE TABLE platform.read_audit_entries (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at timestamptz NOT NULL DEFAULT now(),
    actor_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    resource text NOT NULL,
    resource_id text NULL,
    CONSTRAINT read_audit_entries_resource CHECK (resource IN ('employee', 'payslip', 'tax_form', 'document'))
);
CREATE INDEX read_audit_entries_employee_idx ON platform.read_audit_entries (employee_id, at);
