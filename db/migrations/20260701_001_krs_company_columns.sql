-- Extend the existing company mirror so KRS data can be merged into the same row model.
-- CEIDG-only rows keep their current data; KRS-only rows can be stored without CEIDG identifiers.

alter table ceidg.company_records
    alter column ceidg_id drop not null,
    alter column source_detail_url drop not null,
    alter column last_detail_source_hash drop not null,
    alter column raw_detail_payload drop not null;

alter table ceidg.company_records
    add column if not exists registry_sources text[] not null default array['CEIDG']::text[],
    add column if not exists krs_number text null,
    add column if not exists krs_register_type text null,
    add column if not exists krs_legal_form text null,
    add column if not exists krs_court_name text null,
    add column if not exists krs_registration_date date null,
    add column if not exists krs_last_entry_date date null,
    add column if not exists krs_status text null,
    add column if not exists krs_address jsonb null,
    add column if not exists krs_representatives jsonb null,
    add column if not exists raw_krs_payload jsonb null,
    add column if not exists krs_updated_at_utc timestamptz null;

update ceidg.company_records
set registry_sources = array['CEIDG']::text[]
where registry_sources is null or cardinality(registry_sources) = 0;

create index if not exists ix_company_records_registry_sources on ceidg.company_records using gin (registry_sources);
create index if not exists ix_company_records_krs_number on ceidg.company_records (krs_number);
create index if not exists ix_company_records_krs_legal_form on ceidg.company_records (krs_legal_form);
create index if not exists ix_company_records_krs_status on ceidg.company_records (krs_status);
create index if not exists ix_company_records_krs_registration_date on ceidg.company_records (krs_registration_date);
create index if not exists ix_company_records_raw_krs_payload on ceidg.company_records using gin (raw_krs_payload);
