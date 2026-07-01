-- Mark one current company row per logical identity while preserving CEIDG/KRS history rows.

alter table ceidg.company_records
    add column if not exists is_current boolean not null default false,
    add column if not exists current_rank integer not null default 4;

with ranked as (
    select id,
           case
               when nullif(trim(coalesce(nip, '')), '') is not null then 'nip:' || trim(nip)
               when nullif(trim(coalesce(krs_number, '')), '') is not null then 'krs:' || trim(krs_number)
               when nullif(trim(coalesce(regon, '')), '') is not null then 'regon:' || trim(regon)
               when nullif(trim(coalesce(ceidg_id, '')), '') is not null then 'ceidg:' || upper(trim(ceidg_id))
               else 'id:' || id::text
           end as identity_key,
           case upper(coalesce(status, ''))
               when 'AKTYWNY' then 1
               when 'ACTIVE' then 1
               when 'ZAWIESZONY' then 2
               when 'WYKRESLONY' then 3
               else 4
           end as rank_value,
           row_number() over (
               partition by case
                   when nullif(trim(coalesce(nip, '')), '') is not null then 'nip:' || trim(nip)
                   when nullif(trim(coalesce(krs_number, '')), '') is not null then 'krs:' || trim(krs_number)
                   when nullif(trim(coalesce(regon, '')), '') is not null then 'regon:' || trim(regon)
                   when nullif(trim(coalesce(ceidg_id, '')), '') is not null then 'ceidg:' || upper(trim(ceidg_id))
                   else 'id:' || id::text
               end
               order by
                   case upper(coalesce(status, ''))
                       when 'AKTYWNY' then 1
                       when 'ACTIVE' then 1
                       when 'ZAWIESZONY' then 2
                       when 'WYKRESLONY' then 3
                       else 4
                   end,
                   coalesce(ended_on, removed_on, suspended_on, resumed_on, registered_on, started_on, updated_at_utc::date, date '0001-01-01') desc,
                   updated_at_utc desc,
                   id
           ) as rn
    from ceidg.company_records
)
update ceidg.company_records c
set is_current = ranked.rn = 1,
    current_rank = ranked.rank_value
from ranked
where ranked.id = c.id;

create index if not exists ix_company_records_is_current on ceidg.company_records (is_current);
create index if not exists ix_company_records_current_rank on ceidg.company_records (current_rank);
create index if not exists ix_company_records_current_nip on ceidg.company_records (nip) where is_current and nullif(trim(coalesce(nip, '')), '') is not null;
create index if not exists ix_company_records_current_krs_number on ceidg.company_records (krs_number) where is_current and nullif(trim(coalesce(krs_number, '')), '') is not null;