-- Keep registered_on as the single canonical registration/start date.
-- Existing KRS registration dates are copied there before dropping the duplicate source-specific column.

do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'ceidg'
          and table_name = 'company_records'
          and column_name = 'krs_registration_date'
    ) then
        update ceidg.company_records
        set registered_on = coalesce(registered_on, krs_registration_date)
        where registered_on is null;
    end if;
end $$;

drop index if exists ceidg.ix_company_records_krs_registration_date;

alter table ceidg.company_records
    drop column if exists krs_registration_date;
