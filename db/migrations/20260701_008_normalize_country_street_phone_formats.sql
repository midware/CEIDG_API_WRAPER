-- Tighten formatting rules for country codes, streets and Polish phone numbers.
-- Countries are ISO-2, leading "ul."/"ulica" is removed from street name, and Polish mobile/landline phones use different formats.

create or replace function ceidg._format_polish_phone(nine_digits text)
returns text
language sql
immutable
as $$
    select case
        when left(nine_digits, 2) in ('45', '50', '51', '53', '57', '60', '66', '69', '72', '73', '78', '79', '88')
            then '+48' || nine_digits
        else '+48 ' || substring(nine_digits from 1 for 2) || ' ' || substring(nine_digits from 3 for 3) || ' ' || substring(nine_digits from 6 for 2) || ' ' || substring(nine_digits from 8 for 2)
    end;
$$;

create or replace function ceidg._normalize_phone_list_v2(input text)
returns text
language plpgsql
as $$
declare
    part text;
    digits text;
    chunks text;
    phone text;
    phones text[] := array[]::text[];
    index integer;
begin
    if input is null or btrim(input) = '' then
        return null;
    end if;

    foreach part in array regexp_split_to_array(input, '[,;|/]+') loop
        digits := regexp_replace(coalesce(part, ''), '\D', '', 'g');
        if digits = '' then
            continue;
        end if;

        if left(digits, 2) = '00' then
            digits := substring(digits from 3);
        end if;

        if length(digits) = 9 then
            phone := ceidg._format_polish_phone(digits);
            if not phone = any(phones) then
                phones := array_append(phones, phone);
            end if;
        elsif length(digits) = 11 and left(digits, 2) = '48' then
            phone := ceidg._format_polish_phone(substring(digits from 3));
            if not phone = any(phones) then
                phones := array_append(phones, phone);
            end if;
        elsif length(digits) > 11 and left(digits, 2) = '48' and mod(length(digits) - 2, 9) = 0 then
            chunks := substring(digits from 3);
            index := 1;
            while index + 8 <= length(chunks) loop
                phone := ceidg._format_polish_phone(substring(chunks from index for 9));
                if not phone = any(phones) then
                    phones := array_append(phones, phone);
                end if;
                index := index + 9;
            end loop;
        elsif length(digits) > 9 and mod(length(digits), 9) = 0 then
            index := 1;
            while index + 8 <= length(digits) loop
                phone := ceidg._format_polish_phone(substring(digits from index for 9));
                if not phone = any(phones) then
                    phones := array_append(phones, phone);
                end if;
                index := index + 9;
            end loop;
        elsif length(digits) > 9 then
            phone := '+' || digits;
            if not phone = any(phones) then
                phones := array_append(phones, phone);
            end if;
        end if;
    end loop;

    return nullif(array_to_string(phones, ', '), '');
end;
$$;

update ceidg.company_records
set
    business_address_country = case
        when nullif(btrim(coalesce(business_address_country, '')), '') is null then null
        when upper(btrim(business_address_country)) in ('PL', 'POLSKA', 'RP', 'RZECZPOSPOLITA POLSKA') then 'PL'
        when length(upper(btrim(business_address_country))) = 2 then upper(btrim(business_address_country))
        else upper(btrim(business_address_country))
    end,
    business_address_street = nullif(btrim(regexp_replace(coalesce(business_address_street, ''), '^(ul\.|ulica)\s*', '', 'i')), ''),
    phone = ceidg._normalize_phone_list_v2(phone);

drop function if exists ceidg._normalize_phone_list_v2(text);
drop function if exists ceidg._format_polish_phone(text);