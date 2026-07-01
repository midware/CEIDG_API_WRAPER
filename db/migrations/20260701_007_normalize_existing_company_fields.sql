-- Normalize already imported company profile fields after adding C# write-time normalizers.

create or replace function ceidg._normalize_phone_list(input text)
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
            phone := '+48' || digits;
            if not phone = any(phones) then
                phones := array_append(phones, phone);
            end if;
        elsif length(digits) = 11 and left(digits, 2) = '48' then
            phone := '+' || digits;
            if not phone = any(phones) then
                phones := array_append(phones, phone);
            end if;
        elsif length(digits) > 11 and left(digits, 2) = '48' and mod(length(digits) - 2, 9) = 0 then
            chunks := substring(digits from 3);
            index := 1;
            while index + 8 <= length(chunks) loop
                phone := '+48' || substring(chunks from index for 9);
                if not phone = any(phones) then
                    phones := array_append(phones, phone);
                end if;
                index := index + 9;
            end loop;
        elsif length(digits) > 9 and mod(length(digits), 9) = 0 then
            index := 1;
            while index + 8 <= length(digits) loop
                phone := '+48' || substring(digits from index for 9);
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
    nip = nullif(regexp_replace(coalesce(nip, ''), '\D', '', 'g'), ''),
    regon = nullif(regexp_replace(coalesce(regon, ''), '\D', '', 'g'), ''),
    krs_number = case
        when nullif(regexp_replace(coalesce(krs_number, ''), '\D', '', 'g'), '') is null then null
        else lpad(regexp_replace(krs_number, '\D', '', 'g'), 10, '0')
    end,
    status = nullif(upper(btrim(coalesce(status, ''))), ''),
    phone = ceidg._normalize_phone_list(phone),
    email = nullif(lower(regexp_replace(btrim(coalesce(email, '')), '\s+', '', 'g')), ''),
    website = case
        when nullif(btrim(coalesce(website, '')), '') is null then null
        when lower(btrim(website)) like 'http://%' or lower(btrim(website)) like 'https://%' then lower(rtrim(btrim(website), '/'))
        when position('.' in website) > 0 then 'https://' || lower(rtrim(btrim(website), '/'))
        else nullif(lower(btrim(website)), '')
    end,
    owner_first_name = nullif(initcap(lower(btrim(coalesce(owner_first_name, '')))), ''),
    owner_last_name = nullif(initcap(lower(btrim(coalesce(owner_last_name, '')))), ''),
    owner_nip = nullif(regexp_replace(coalesce(owner_nip, ''), '\D', '', 'g'), ''),
    owner_regon = nullif(regexp_replace(coalesce(owner_regon, ''), '\D', '', 'g'), ''),
    legal_form = nullif(replace(replace(replace(initcap(lower(btrim(coalesce(legal_form, '')))), ' Z ', ' z '), ' W ', ' w '), ' I ', ' i '), ''),
    business_address_country = nullif(upper(btrim(coalesce(business_address_country, ''))), ''),
    business_address_voivodeship = case upper(btrim(coalesce(business_address_voivodeship, '')))
        when 'DOLNOŚLĄSKIE' then 'Dolnośląskie'
        when 'DOLNOSLASKIE' then 'Dolnośląskie'
        when 'KUJAWSKO-POMORSKIE' then 'Kujawsko-Pomorskie'
        when 'LUBELSKIE' then 'Lubelskie'
        when 'LUBUSKIE' then 'Lubuskie'
        when 'ŁÓDZKIE' then 'Łódzkie'
        when 'LODZKIE' then 'Łódzkie'
        when 'MAŁOPOLSKIE' then 'Małopolskie'
        when 'MALOPOLSKIE' then 'Małopolskie'
        when 'MAZOWIECKIE' then 'Mazowieckie'
        when 'OPOLSKIE' then 'Opolskie'
        when 'PODKARPACKIE' then 'Podkarpackie'
        when 'PODLASKIE' then 'Podlaskie'
        when 'POMORSKIE' then 'Pomorskie'
        when 'ŚLĄSKIE' then 'Śląskie'
        when 'SLASKIE' then 'Śląskie'
        when 'ŚWIĘTOKRZYSKIE' then 'Świętokrzyskie'
        when 'SWIETOKRZYSKIE' then 'Świętokrzyskie'
        when 'WARMIŃSKO-MAZURSKIE' then 'Warmińsko-Mazurskie'
        when 'WARMINSKO-MAZURSKIE' then 'Warmińsko-Mazurskie'
        when 'WIELKOPOLSKIE' then 'Wielkopolskie'
        when 'ZACHODNIOPOMORSKIE' then 'Zachodniopomorskie'
        when '' then null
        else initcap(lower(btrim(business_address_voivodeship)))
    end,
    business_address_county = nullif(initcap(lower(btrim(coalesce(business_address_county, '')))), ''),
    business_address_municipality = nullif(initcap(lower(btrim(coalesce(business_address_municipality, '')))), ''),
    business_address_city = nullif(initcap(lower(btrim(coalesce(business_address_city, '')))), ''),
    business_address_street = nullif(
        regexp_replace(
            regexp_replace(
                regexp_replace(
                    regexp_replace(
                        regexp_replace(initcap(lower(btrim(coalesce(business_address_street, '')))), '^Ul\.', 'ul.'),
                        '^Al\.',
                        'al.'
                    ),
                    '^Pl\.',
                    'pl.'
                ),
                '^Bulw\.',
                'bulw.'
            ),
            '^Os\.',
            'os.'
        ),
        ''
    ),
    business_address_postal_code = case
        when length(regexp_replace(coalesce(business_address_postal_code, ''), '\D', '', 'g')) = 5
            then substring(regexp_replace(business_address_postal_code, '\D', '', 'g') from 1 for 2) || '-' || substring(regexp_replace(business_address_postal_code, '\D', '', 'g') from 3 for 3)
        else nullif(btrim(coalesce(business_address_postal_code, '')), '')
    end,
    main_pkd_code = nullif(upper(regexp_replace(coalesce(main_pkd_code, ''), '[^0-9A-Za-z]', '', 'g')), ''),
    updated_at_utc = updated_at_utc;

drop function if exists ceidg._normalize_phone_list(text);
