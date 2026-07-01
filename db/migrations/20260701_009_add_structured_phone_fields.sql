-- Add structured phone fields for analytics while keeping the legacy phone text column.

alter table ceidg.company_records
    add column if not exists phone_mobile text null,
    add column if not exists phone_landline text null,
    add column if not exists phones_json jsonb null;

create index if not exists ix_company_records_phone_mobile on ceidg.company_records (phone_mobile);
create index if not exists ix_company_records_phone_landline on ceidg.company_records (phone_landline);
create index if not exists ix_company_records_phones_json on ceidg.company_records using gin (phones_json);

with parsed as (
    select c.id,
           nullif(string_agg(p.phone, ', ' order by p.ordinal) filter (where p.phone ~ '^\+48[0-9]{9}$'), '') as mobile,
           nullif(string_agg(p.phone, ', ' order by p.ordinal) filter (where p.phone ~ '^\+48 [0-9]{2} [0-9]{3} [0-9]{2} [0-9]{2}$'), '') as landline,
           jsonb_agg(
               jsonb_build_object(
                   'number', p.phone,
                   'type', case
                       when p.phone ~ '^\+48[0-9]{9}$' then 'mobile'
                       when p.phone ~ '^\+48 [0-9]{2} [0-9]{3} [0-9]{2} [0-9]{2}$' then 'landline'
                       else 'unknown'
                   end,
                   'countryCode', case when p.phone like '+48%' then 'PL' else null end
               )
               order by p.ordinal
           ) filter (where p.phone <> '') as phones
    from ceidg.company_records c
    cross join lateral regexp_split_to_table(coalesce(c.phone, ''), '\s*,\s*') with ordinality as p(phone, ordinal)
    where nullif(trim(coalesce(c.phone, '')), '') is not null
    group by c.id
)
update ceidg.company_records c
set phone_mobile = parsed.mobile,
    phone_landline = parsed.landline,
    phones_json = parsed.phones
from parsed
where parsed.id = c.id;