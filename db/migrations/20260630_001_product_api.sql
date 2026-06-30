-- Adds product API authentication, API key and token billing tables.
-- Safe to run multiple times.

create schema if not exists app;

create table if not exists app.api_users (
    id uuid primary key,
    email text not null,
    email_normalized text not null unique,
    password_hash text not null,
    display_name text null,
    token_balance bigint not null default 0,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    disabled_at_utc timestamptz null
);

create table if not exists app.api_keys (
    id uuid primary key,
    user_id uuid not null references app.api_users(id) on delete cascade,
    key_prefix text not null,
    key_hash text not null unique,
    name text null,
    created_at_utc timestamptz not null default now(),
    last_used_at_utc timestamptz null,
    expires_at_utc timestamptz null,
    revoked_at_utc timestamptz null
);

create index if not exists ix_api_keys_user_id on app.api_keys(user_id);
create index if not exists ix_api_keys_active_hash on app.api_keys(key_hash) where revoked_at_utc is null;

create table if not exists app.token_ledger (
    id bigserial primary key,
    user_id uuid not null references app.api_users(id) on delete cascade,
    delta bigint not null,
    balance_after bigint not null,
    reason text not null,
    request_id uuid null,
    metadata jsonb not null default '{}'::jsonb,
    created_at_utc timestamptz not null default now()
);

create index if not exists ix_token_ledger_user_created on app.token_ledger(user_id, created_at_utc desc);

create table if not exists app.api_query_log (
    id uuid primary key,
    user_id uuid not null references app.api_users(id) on delete cascade,
    endpoint text not null,
    selected_columns text[] not null,
    page integer not null,
    page_size integer not null,
    returned_rows integer not null,
    token_cost bigint not null,
    created_at_utc timestamptz not null default now()
);

create index if not exists ix_api_query_log_user_created on app.api_query_log(user_id, created_at_utc desc);
