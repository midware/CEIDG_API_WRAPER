-- Add canonical company-profile fields shared by CEIDG and KRS imports.
-- Source-specific KRS columns stay as audit/source metadata; API reads should prefer these shared columns.

alter table ceidg.company_records
    add column if not exists legal_form text null,
    add column if not exists registered_on date null;

update ceidg.company_records
set legal_form = coalesce(
        nullif(legal_form, ''),
        nullif(krs_legal_form, ''),
        case when registry_sources @> array['CEIDG']::text[] then 'JEDNOOSOBOWA DZIAŁALNOŚĆ GOSPODARCZA' end
    ),
    registered_on = coalesce(registered_on, krs_registration_date, started_on)
where legal_form is null
   or legal_form = ''
   or legal_form = 'JEDNOOSOBOWA DZIALALNOSC GOSPODARCZA'
   or registered_on is null;

update ceidg.company_records
set business_address_country = coalesce(nullif(business_address_country, ''), krs_address #>> '{adres,kraj}', krs_address #>> '{siedziba,kraj}'),
    business_address_voivodeship = coalesce(nullif(business_address_voivodeship, ''), krs_address #>> '{siedziba,wojewodztwo}', krs_address #>> '{adres,wojewodztwo}'),
    business_address_county = coalesce(nullif(business_address_county, ''), krs_address #>> '{siedziba,powiat}', krs_address #>> '{adres,powiat}'),
    business_address_municipality = coalesce(nullif(business_address_municipality, ''), krs_address #>> '{siedziba,gmina}', krs_address #>> '{adres,gmina}'),
    business_address_city = coalesce(nullif(business_address_city, ''), krs_address #>> '{adres,miejscowosc}', krs_address #>> '{siedziba,miejscowosc}', krs_address #>> '{adres,poczta}'),
    business_address_street = coalesce(nullif(business_address_street, ''), krs_address #>> '{adres,ulica}'),
    business_address_building = coalesce(nullif(business_address_building, ''), krs_address #>> '{adres,nrDomu}'),
    business_address_unit = coalesce(nullif(business_address_unit, ''), krs_address #>> '{adres,nrLokalu}'),
    business_address_postal_code = coalesce(nullif(business_address_postal_code, ''), krs_address #>> '{adres,kodPocztowy}')
where raw_krs_payload is not null;

with krs_pkd as (
    select id,           case when jsonb_typeof(raw_krs_payload #> '{odpis,dane,dzial3,przedmiotDzialalnosci,przedmiotPrzewazajacejDzialalnosci}') = 'array' then raw_krs_payload #> '{odpis,dane,dzial3,przedmiotDzialalnosci,przedmiotPrzewazajacejDzialalnosci}' else '[]'::jsonb end as main_items,
           case when jsonb_typeof(raw_krs_payload #> '{odpis,dane,dzial3,przedmiotDzialalnosci,przedmiotPozostalejDzialalnosci}') = 'array' then raw_krs_payload #> '{odpis,dane,dzial3,przedmiotDzialalnosci,przedmiotPozostalejDzialalnosci}' else '[]'::jsonb end as other_items
    from ceidg.company_records
    where raw_krs_payload is not null
), normalized as (
    select id, true as is_main, value as item
    from krs_pkd, jsonb_array_elements(coalesce(main_items, '[]'::jsonb)) as value
    union all
    select id, false as is_main, value as item
    from krs_pkd, jsonb_array_elements(coalesce(other_items, '[]'::jsonb)) as value
), aggregated as (
    select id,
           coalesce(
               min(concat(item->>'kodDzial', item->>'kodKlasa', item->>'kodPodklasa')) filter (where is_main),
               min(concat(item->>'kodDzial', item->>'kodKlasa', item->>'kodPodklasa'))
           ) as main_pkd_code,
           jsonb_agg(jsonb_build_object(
               'kod', concat(item->>'kodDzial', item->>'kodKlasa', item->>'kodPodklasa'),
               'nazwa', item->>'opis',
               'main', is_main
           ) order by is_main desc) as pkd_codes
    from normalized
    where nullif(concat(item->>'kodDzial', item->>'kodKlasa'), '') is not null
    group by id
)
update ceidg.company_records c
set main_pkd_code = coalesce(nullif(c.main_pkd_code, ''), aggregated.main_pkd_code),
    pkd_codes = coalesce(c.pkd_codes, aggregated.pkd_codes)
from aggregated
where c.id = aggregated.id;

create index if not exists ix_company_records_legal_form on ceidg.company_records (legal_form);
create index if not exists ix_company_records_registered_on on ceidg.company_records (registered_on);
