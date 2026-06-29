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
    nip text null,
    payload_kind text not null,
    request_uri text not null,
    status_code integer not null,
    content_hash text not null,
    payload jsonb not null,
    fetched_at_utc timestamptz not null,
    import_run_id uuid null references source.import_run(id),
    constraint ck_raw_company_payload_kind check (payload_kind in ('index', 'detail', 'change'))
);

create index if not exists ix_raw_company_payload_ceidg_id
    on source.raw_company_payload (ceidg_id);

create index if not exists ix_raw_company_payload_nip
    on source.raw_company_payload (nip);

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
    phone text null,
    email text null,
    website text null,
    main_pkd_code text null,
    last_index_source_hash text null,
    last_detail_source_hash text not null,
    updated_at_utc timestamptz not null
);

create index if not exists ix_company_nip on ceidg.company (nip);
create index if not exists ix_company_regon on ceidg.company (regon);
create index if not exists ix_company_status on ceidg.company (status);
create index if not exists ix_company_main_pkd_code on ceidg.company (main_pkd_code);

create table if not exists ceidg.pkd_code (
    code text primary key,
    description text null
);

create table if not exists ceidg.company_pkd (
    company_id uuid not null references ceidg.company(id) on delete cascade,
    pkd_code text not null references ceidg.pkd_code(code),
    is_primary boolean not null default false,
    source_order integer not null default 0,
    primary key (company_id, pkd_code)
);

create index if not exists ix_company_pkd_pkd_code on ceidg.company_pkd (pkd_code);
