-- Consolidate KRS identifiers into canonical company columns.
-- NIP, REGON and name are global company attributes, not source-specific KRS fields.

do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'ceidg'
          and table_name = 'company_records'
          and column_name = 'krs_nip'
    ) then
        update ceidg.company_records
        set nip = coalesce(nullif(nip, ''), nullif(krs_nip, '')),
            regon = coalesce(nullif(regon, ''), nullif(krs_regon, '')),
            name = coalesce(nullif(name, ''), nullif(krs_name, ''))
        where (nip is null or nip = '')
           or (regon is null or regon = '')
           or (name is null or name = '');
    end if;
end $$;

drop index if exists ceidg.ix_company_records_krs_nip;
drop index if exists ceidg.ix_company_records_krs_regon;

alter table ceidg.company_records
    drop column if exists krs_nip,
    drop column if exists krs_regon,
    drop column if exists krs_name;
