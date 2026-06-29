-- Creates the local CEIDG mirror database and all required schemas/tables.
-- Run with psql connected to the maintenance database, for example:
-- psql -h localhost -p 5432 -U postgres -d postgres -f db/setup_local_database.sql

select 'create database ceidg_mirror owner postgres encoding ''UTF8''' as command
where not exists (
    select 1
    from pg_database
    where datname = 'ceidg_mirror'
)\gexec

\connect ceidg_mirror

create schema if not exists source;
create schema if not exists ceidg;

create table if not exists source.import_run (
    id uuid primary key,
    import_kind text not null,
    started_at_utc timestamptz not null,
    finished_at_utc timestamptz null,
    status text not null,
    details jsonb not null default '{}'::jsonb
);

create table if not exists ceidg.company_records (
    id uuid primary key,

    -- Source identity and synchronization metadata
    ceidg_id text not null unique,
    source_index_url text null,
    source_detail_url text not null,
    last_index_source_hash text null,
    last_detail_source_hash text not null,
    first_seen_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    last_import_run_id uuid null references source.import_run(id),

    -- Full source payloads. These JSONB columns are mandatory so no CEIDG field is lost
    -- when the public schema changes or contains nested collections.
    raw_index_payload jsonb null,
    raw_detail_payload jsonb not null,

    -- firma.* scalar fields from the documented /firma response
    nip text null,
    regon text null,
    name text null,
    status text null,
    status_number integer null,
    phone text null,
    email text null,
    website text null,
    electronic_delivery_address text null,
    other_contact_form text null,
    marital_property_community integer null,
    marital_property_community_ended_on date null,
    started_on date null,
    suspended_on date null,
    ended_on date null,
    removed_on date null,
    resumed_on date null,
    owner_death_on date null,
    succession_management_established_on date null,
    succession_management_expired_on date null,
    detail_link text null,

    -- firma.wlasciciel.*
    owner_first_name text null,
    owner_last_name text null,
    owner_nip text null,
    owner_nip_revoked boolean null,
    owner_nip_invalidated boolean null,
    owner_regon text null,

    -- Commonly queried address/contact/search fields extracted from nested objects
    business_address_country text null,
    business_address_voivodeship text null,
    business_address_county text null,
    business_address_municipality text null,
    business_address_city text null,
    business_address_street text null,
    business_address_building text null,
    business_address_unit text null,
    business_address_postal_code text null,
    business_address_post_office_box text null,
    business_address_unusual_place_description text null,
    business_address_recipient text null,
    business_address_terc text null,
    business_address_simc text null,
    business_address_ulic text null,

    main_pkd_code text null,

    -- Nested/list sections from the documented /firma response, kept in the same table.
    citizenships jsonb null,
    legal_removal_bases jsonb null,
    correspondence_address jsonb null,
    pkd_codes jsonb null,
    civil_partnerships jsonb null,
    additional_business_addresses jsonb null,
    bans jsonb null,
    bankruptcy jsonb null,
    succession_manager jsonb null,
    professional_qualifications jsonb null,
    permissions jsonb null,
    restrictions jsonb null,
    legal_capacity_restrictions jsonb null,

    -- Extraction/audit diagnostics
    extraction_version integer not null default 1,
    extraction_warnings jsonb not null default '[]'::jsonb
);

create index if not exists ix_company_records_nip on ceidg.company_records (nip);
create index if not exists ix_company_records_regon on ceidg.company_records (regon);
create index if not exists ix_company_records_owner_nip on ceidg.company_records (owner_nip);
create index if not exists ix_company_records_owner_regon on ceidg.company_records (owner_regon);
create index if not exists ix_company_records_status on ceidg.company_records (status);
create index if not exists ix_company_records_main_pkd_code on ceidg.company_records (main_pkd_code);
create index if not exists ix_company_records_city on ceidg.company_records (business_address_city);
create index if not exists ix_company_records_voivodeship on ceidg.company_records (business_address_voivodeship);
create index if not exists ix_company_records_raw_detail_payload on ceidg.company_records using gin (raw_detail_payload);
create index if not exists ix_company_records_pkd_codes on ceidg.company_records using gin (pkd_codes);


create table if not exists source.report_payload (
    id uuid primary key,
    ceidg_report_id text not null unique,
    request_uri text not null,
    status_code integer not null,
    content_hash text not null,
    payload jsonb not null,
    fetched_at_utc timestamptz not null,
    import_run_id uuid null references source.import_run(id)
);

create index if not exists ix_report_payload_content_hash on source.report_payload (content_hash);
create index if not exists ix_report_payload_payload on source.report_payload using gin (payload);

create table if not exists source.report_company_link (
    id bigserial primary key,
    report_id uuid not null references source.report_payload(id) on delete cascade,
    company_record_id uuid null references ceidg.company_records(id) on delete set null,
    ceidg_id text null,
    nip text null,
    regon text null,
    linked_at_utc timestamptz not null,
    link_status text not null,
    link_warnings jsonb not null default '[]'::jsonb,
    constraint ck_report_company_link_status check (link_status in ('linked', 'unmatched', 'ambiguous'))
);

create unique index if not exists ux_report_company_link_identity
    on source.report_company_link (
        report_id,
        coalesce(ceidg_id, ''),
        coalesce(nip, ''),
        coalesce(regon, '')
    );

create index if not exists ix_report_company_link_company_record_id on source.report_company_link (company_record_id);
create index if not exists ix_report_company_link_nip on source.report_company_link (nip);
create index if not exists ix_report_company_link_regon on source.report_company_link (regon);
create index if not exists ix_report_company_link_ceidg_id on source.report_company_link (ceidg_id);



