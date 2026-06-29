# CEIDG API v2 Field Coverage Audit

## Decision

Company data is stored in one PostgreSQL table: `ceidg.company_records`.

The table keeps queryable scalar columns for common business/search fields and mandatory JSONB columns for complete source fidelity. The canonical full company copy is `raw_detail_payload`; this column must contain the entire `/firma` response for the company. No field from CEIDG may be discarded just because it is nested, repeated, rare, or not yet used by the product API.

## Endpoint Coverage

Implemented CEIDG API v2 client methods:

- `GET /firmy` - index/search page only, not a complete company record.
- `GET /firma?nip=...` - full company details by NIP.
- `GET /firma?regon=...` - full company details by REGON.
- `GET /firma/{id}` - full company details by CEIDG id.
- `GET /raporty` - report list.
- `GET /raport/{id}` - selected report.
- `GET /zmiana` - changed company identifiers.

## Fields Previously Not Covered By Columns

The earlier schema only covered a small subset: CEIDG id, NIP, REGON, name, status, start date, phone, email, website, and main PKD. According to the `/firma` documentation, these fields/sections also have to be preserved:

- owner NIP revoked/invalidated flags
- full business address: country, post office box, unusual place description, recipient, TERC, SIMC, ULIC
- citizenships
- status number
- suspension, termination, deletion, and resumption dates
- electronic delivery address
- other contact form
- marital property community and end date
- owner death date
- succession management dates
- legal removal bases
- correspondence address
- full PKD list
- civil partnership data
- additional business addresses
- bans
- bankruptcy/restructuring information
- succession manager data, contact data, citizenships, and correspondence address
- professional qualifications
- permissions/licenses
- restrictions
- legal capacity restrictions and curator data
- detail link

## Storage Rule

All fields above are preserved in `ceidg.company_records.raw_detail_payload` even if they are also extracted into scalar or JSONB helper columns.

High-value nested/list sections are also copied into same-row JSONB columns:

- `citizenships`
- `legal_removal_bases`
- `correspondence_address`
- `pkd_codes`
- `civil_partnerships`
- `additional_business_addresses`
- `bans`
- `bankruptcy`
- `succession_manager`
- `professional_qualifications`
- `permissions`
- `restrictions`
- `legal_capacity_restrictions`

## Import Rule

A company is complete only after detail hydration from `/firma?nip=...`, `/firma?regon=...`, or `/firma/{id}`. `/firmy` may seed the queue, but it must not be treated as the final company snapshot.
