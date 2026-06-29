create schema if not exists source;
create schema if not exists ceidg;
create schema if not exists history;
create schema if not exists app;

create table if not exists source.import_run (
    id uuid primary key,
    import_kind text not null,
    started_at_utc timestamptz not null,
    finished_at_utc timestamptz null,
    status text not null,
    details jsonb not null default '{}'::jsonb
);

create table if not exists source.raw_company_payload (
    id bigserial primary key,
    ceidg_id text null,
    request_uri text not null,
    status_code integer not null,
    content_hash text not null,
    payload jsonb not null,
    fetched_at_utc timestamptz not null,
    import_run_id uuid null references source.import_run(id)
);

create index if not exists ix_raw_company_payload_ceidg_id
    on source.raw_company_payload (ceidg_id);

create unique index if not exists ux_raw_company_payload_hash
    on source.raw_company_payload (content_hash);

create table if not exists ceidg.company (
    id uuid primary key,
    ceidg_id text not null unique,
    nip text null,
    regon text null,
    name text not null,
    status text null,
    started_on date null,
    last_source_hash text not null,
    updated_at_utc timestamptz not null
);

create index if not exists ix_company_nip on ceidg.company (nip);
create index if not exists ix_company_regon on ceidg.company (regon);
create index if not exists ix_company_status on ceidg.company (status);
