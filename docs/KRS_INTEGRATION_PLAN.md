# Plan integracji KRS z leadbase.network

Cel: zaciagac dane KRS do tej samej bazy i tego samego modelu firm, ktory dzis obsluguje CEIDG. Nie tworzymy osobnej bazy ani rownoleglego zestawu tabel firmowych. KRS ma uzupelniac i rozszerzac rekord firmy.

## Zasada modelu danych

Glowna tabela pozostaje `ceidg.company_records`, ale traktujemy ja jako ogolny rejestr podmiotow gospodarczych. Nazwa schematu jest historyczna; na pozniejszym etapie mozna rozwazyc rename na `registry.company_records`, ale nie jest to wymagane do integracji.

Nowe dane KRS dokladamy jako kolumny w tej samej tabeli:

- `registry_sources text[]` - np. `CEIDG`, `KRS`.
- `krs_number text null`.
- `krs_register_type text null` - np. przedsiebiorcy, stowarzyszenia.
- `krs_legal_form text null`.
- `krs_court_name text null`.
- `krs_registration_date date null`.
- `krs_last_entry_date date null`.
- `krs_status text null`.
- `krs_address jsonb null`.
- `krs_representatives jsonb null`.
- `krs_beneficiaries jsonb null` tylko jesli API KRS/API powiazane legalnie to udostepnia i potwierdzimy zakres.
- `raw_krs_payload jsonb null`.
- `krs_updated_at_utc timestamptz null`.

Nie nadpisujemy bezrefleksyjnie CEIDG danymi KRS. Pola wspolne mapujemy wedlug priorytetow:

- `nip`, `regon` jako klucze laczenia i wyszukiwania.
- `nip`, `regon` i `name` sa kanonicznymi polami podmiotu, niezaleznie od zrodla CEIDG/KRS. Nie duplikujemy ich jako `krs_nip`, `krs_regon`, `krs_name`.
- `status` pozostaje ujednoliconym statusem produktu, a `krs_status` przechowuje status zrodlowy.

## Laczenie rekordow

Priorytet dopasowania:

1. `krs_number` gdy rekord juz istnieje z poprzedniego importu KRS.
2. `nip` zgodny z kanonicznym `nip`.
3. `regon` zgodny z kanonicznym `regon`.
4. Dopasowanie po nazwie tylko jako kandydat do audytu, nie jako automatyczny merge.

Jesli KRS zwroci podmiot bez NIP/REGON, tworzymy rekord w tej samej tabeli z `krs_number`, kanonicznym `name`, `registry_sources = ARRAY['KRS']` i pustymi polami CEIDG.

## Worker KRS

Dodajemy nowy tryb importu w istniejacym workerze:

- `Import:Source = KrsApi` albo osobna sekcja `KrsImport`.
- checkpoint po stronie `source.import_checkpoint` lub obecnego mechanizmu import-run.
- osobny klient HTTP `KrsClient`.
- rate limiter analogiczny do CEIDG.
- retry z backoffem na 429/5xx.
- zapis raw payloadu do `raw_krs_payload`.

Tryby pobierania:

- import po numerach KRS, NIP lub REGON z listy wejsciowej;
- import uzupelniajacy dla firm CEIDG, ktore maja NIP/REGON, ale nie maja `krs_number`;
- import zmian, jesli OpenAPI KRS udostepnia endpoint zmian/ostatniej aktualizacji.

## API produktu

`GET /companies` pozostaje glownym endpointem. Dodajemy kolumny wybieralne:

- `krsNumber`
- `krsLegalForm`
- `krsStatus`
- `krsRegistrationDate`
- `krsLastEntryDate`
- `krsCourtName`
- `registrySources`

Dodajemy filtry:

- `krsNumber`
- `registrySource`
- `legalForm`
- `hasKrs`

Analityka `/analytics` moze dostac nowe wymiary:

- `registrySource`
- `krsLegalForm`
- `krsStatus`
- `krsRegistrationYear`

## Migracje SQL

Pierwsza migracja KRS powinna tylko rozszerzyc istniejaca tabele:

```sql
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

create index if not exists ix_company_records_krs_number on ceidg.company_records(krs_number);
create index if not exists ix_company_records_registry_sources on ceidg.company_records using gin(registry_sources);
create index if not exists ix_company_records_krs_legal_form on ceidg.company_records(krs_legal_form);
```

## Etapy pracy

1. Pobrac i zamrozic aktualna specyfikacje OpenAPI KRS z PRS.
2. Wygenerowac albo recznie napisac minimalnego klienta `KrsClient`.
3. Zrobic migracje kolumn KRS w `ceidg.company_records`.
4. Dodac mapper KRS -> `CompanyRecord`.
5. Dodac tryb workera `KrsApi`.
6. Dodac testy mapowania i idempotentnego upsertu po NIP/REGON/KRS.
7. Rozszerzyc `CompanyColumnCatalog` i filtry `/companies`.
8. Rozszerzyc `/analytics` o wymiary KRS.
9. Uruchomic import testowy na malej probce.
10. Dopiero potem wlaczyc import uzupelniajacy dla calej bazy.

## Uwagi operacyjne

Portal PRS wskazuje publiczny host OpenAPI jako `https://api-krs.ms.gov.pl/`. Wlasciwe endpointy i limity trzeba potwierdzic po pobraniu specyfikacji OpenAPI przed implementacja klienta produkcyjnego.
