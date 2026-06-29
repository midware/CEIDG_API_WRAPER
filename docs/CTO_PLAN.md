# CTO Plan: CEIDG PostgreSQL Mirror and Analytical API

## 1. Product Goal

Build a private C#/.NET data platform that periodically copies CEIDG business registry data into our own PostgreSQL database, then exposes clean, fast API endpoints for search, enrichment, segmentation, scoring, exports, and analytics similar in category to dataport.pl.

The first release must focus on source-of-truth ingestion and data quality. Commercial/API features come after we can reliably answer: what was fetched, when it was fetched, from which CEIDG endpoint, how it changed, and whether the local record is current.

## 2. Source API Assessment

Primary integration target:

- CEIDG API v2 Hurtowni Danych and Biznes.gov.pl.
- Test base URL: `https://test-dane.biznes.gov.pl/api/ceidg/v2`
- Production base URL: `https://dane.biznes.gov.pl/api/ceidg/v2`
- Transport: HTTPS, TLS 1.2, OpenAPI/.NET Core style REST API.
- Auth: JWT token in `Authorization: Bearer {JWT-TOKEN}`.

Important API v2 methods from the documentation:

- `GET /firmy` returns only a paginated search/index view of companies matching filters such as NIP, REGON, owner name, company name, address, PKD, date range, and status. It is not a full company record.
- `GET /firma?nip=...`, `GET /firma?regon=...`, or `GET /firma/{id}` returns detailed company data. This detail call is required for fields such as phone, email, website, full PKD list, main PKD, restrictions, qualifications, licenses, insolvency, and succession sections.
- `GET /raporty` returns available reports.
- `GET /raport/{id}` returns a selected report.
- `GET /zmiana` returns a paginated list of company identifiers changed in a date range.

Hard operational limits:

- 50 requests per 3 minutes.
- 1000 requests per 60 minutes.
- The safe steady pace is about one request every 3.6 seconds.
- `429` must trigger a cool-down, not aggressive retrying.

Legacy fallback/reference:

- Older SOAP DataStore API exposes `GetID` and `GetMigrationDataExtendedAddressInfo`.
- It returns XML and supports filters including NIP, REGON, NIP/REGON of civil partnerships, address filters, PKD, status, and migration date range.
- We should not start with SOAP unless API v2 proves insufficient for full historical import.

## 3. Strategic Architecture

Use a single-company-table data model:

- `ceidg.company_records` is the only canonical table that stores unique company data.
- One CEIDG company equals one row in `ceidg.company_records` whenever a CEIDG id/NIP/REGON can identify it.
- The row contains scalar columns for common filters plus JSONB columns for nested/list sections.
- `raw_detail_payload jsonb` is mandatory and stores the full `/firma` response, so every CEIDG field is preserved even if we have not extracted it yet.
- `/firmy` data may be stored as `raw_index_payload`, but it is only an index/search snapshot and never the canonical company copy.
- Report data may use auxiliary `source.report_payload` and `source.report_company_link` tables if reports cannot be represented as company columns. These tables are source/import artifacts, not the canonical company model. They must link back to `ceidg.company_records` by `ceidg_id`, NIP, REGON, or `company_record_id` when possible.

The ingestion service upserts one row per company after detail hydration. A company is not complete until `/firma?nip=...`, `/firma?regon=...`, or `/firma/{id}` has been fetched and stored.

## 4. Proposed .NET Solution Layout

Create one solution with clear service boundaries:

- `CeidgMirror.Api` - public/internal API over PostgreSQL.
- `CeidgMirror.Worker` - background ingestion and synchronization service.
- `CeidgMirror.Infrastructure` - PostgreSQL, HTTP clients, rate limiting, persistence.
- `CeidgMirror.Domain` - company record domain model and business rules.
- `CeidgMirror.Application` - import orchestration, commands, queries, validation.
- `CeidgMirror.Contracts` - DTOs for API responses and CEIDG source models.
- `CeidgMirror.Tests` - unit and integration tests.

Recommended stack:

- .NET 9 or latest LTS available in the target deployment environment.
- ASP.NET Core Minimal APIs or Controllers.
- Dapper or Npgsql bulk APIs for high-volume upserts into the single company table.
- PostgreSQL JSONB for full CEIDG payload fidelity and GIN indexes for selective raw/nested queries.
- Hangfire, Quartz.NET, or a hosted worker with durable import job tables.
- OpenTelemetry, Serilog, and structured metrics from day one.

## 5. PostgreSQL Schema Strategy

Initial schemas:

- `ceidg` for the single current company table.
- `source` for import-run metadata and raw report artifacts. It must not become a duplicate canonical company store.
- `app` for API users, keys, saved searches, exports, and product-specific data later.

Critical tables:

- `source.import_run`
- `source.report_payload`
- `source.report_company_link`
- `ceidg.company_records`

Identity and idempotency:

- Use CEIDG `id` as the main external identifier when available.
- Keep NIP and REGON as indexed columns on `ceidg.company_records`, not separate identifier tables.
- Use detail payload hash to skip unchanged records.
- Store every import run so re-runs are safe.

## 6. Import Strategy

Phase 1: API access and schema discovery

- Obtain/confirm JWT credentials for test and production.
- Generate or hand-write typed clients from observed API v2 JSON.
- Fetch sample `/firmy`, `/firma/{id}`, `/raporty`, `/raport/{id}`, and `/zmiana` responses.
- Save golden sample payloads under tests.

Phase 2: Full initial load

- Prefer official reports from `/raporty` and `/raport/{id}` if they provide bulk/full datasets. Store report payloads separately only when their shape is report-level rather than one-company-per-row, then link report items to `ceidg.company_records` by CEIDG id, NIP, or REGON.
- If reports are not suitable, partition `/firmy` requests by deterministic criteria:
  - date ranges,
  - statuses,
  - possibly PKD/address partitions only if needed.
- For each listed company/NIP from `/firmy` or `/zmiana`, fetch detailed `/firma?nip=...` or `/firma/{id}` before treating the record as complete. The importer must never persist the `/firmy` response alone as the canonical company snapshot.
- Run with a global distributed rate limiter set below the documented API ceilings.

Phase 3: Incremental synchronization

- Use `/zmiana?dataod=YYYY-MM-DD&datado=YYYY-MM-DD&page=...&limit=...`.
- For every changed identifier, refetch detailed company data.
- Upsert the single `ceidg.company_records` row for every changed company and keep change metadata inside import-run details or same-row payload metadata until a separate audit requirement appears.
- Maintain checkpoints by date window and page.
- Reconcile daily, weekly, and monthly to catch missed changes.

Phase 4: Reprocessing and enrichment

- Re-run transformations from raw payloads after schema/model changes.
- Add analytics projections and materialized views.
- Add search indexes and API-specific read helpers over `ceidg.company_records`.

## 7. API Product Roadmap

MVP endpoints:

- Search companies by NIP, REGON, name, status, PKD, city, voivodeship, and date ranges.
- Company detail endpoint backed by the full `ceidg.company_records` row and `raw_detail_payload`.
- Change history endpoint.
- Export endpoint for CSV/XLSX batches.
- Basic analytics endpoint: counts by PKD, region, status, creation date.

Commercial/product features after data reliability:

- Lead lists and segmentation.
- Alerts on company changes.
- Enrichment scoring.
- Saved searches.
- API keys, quotas, usage analytics.
- Audit trail for exported data.

## 8. Reliability and Compliance Principles

- Respect CEIDG API rate limits strictly.
- Do not assume the public API permits unlimited replication; confirm terms, license, and any commercial usage obligations before production-scale crawling.
- Keep raw source payloads for auditability.
- Keep a deletion/update strategy for records that disappear or become legally restricted.
- Avoid storing or exposing unnecessary personal data in product endpoints.
- Document data provenance in every public API response.

## 9. First Implementation Milestones

Milestone 0: Repository foundation

- Create private GitHub repository.
- Add solution skeleton, README, architecture docs, `.gitignore`.
- Add Docker Compose for PostgreSQL.

Milestone 1: CEIDG API probe

- Add typed `CeidgApiClient`.
- Add JWT configuration.
- Add resilient `HttpClient` pipeline with rate limiter and retry policy.
- Fetch and persist sample payloads from test API.

Milestone 2: Database foundation

- Add PostgreSQL schema migrations.
- Add the single `ceidg.company_records` table.
- Add import run metadata and report payload/link tables only for report artifacts.
- Ensure `raw_detail_payload` preserves every `/firma` field.

Milestone 3: Initial importer

- Implement paginated `/firmy` index import.
- Implement mandatory detail hydration through `/firma?nip=...` or `/firma/{id}` for every discovered company, including website, phone, email, PKD list, and main PKD.
- Persist one complete `ceidg.company_records` row per detailed company response.
- Add idempotent upserts and payload hashing.

Milestone 4: Incremental sync

- Implement `/zmiana` synchronization.
- Add checkpoint recovery after failures.
- Add metrics: imported, updated, skipped, failed, rate-limited.

Milestone 5: API MVP

- Add read API over PostgreSQL.
- Add indexes and performance tests for common queries.
- Add export endpoint.
- Add authentication for our own API.

## 10. Immediate Next Step

Start with a small proof of concept:

1. Create the .NET solution skeleton.
2. Add PostgreSQL Docker Compose.
3. Add a CEIDG API client with JWT configuration and a conservative rate limiter.
4. Fetch one page from `/firmy`, extract NIP/id values, then fetch detail records from `/firma?nip=...` or `/firma/{id}` in the test environment.
5. Persist the first complete company into one `ceidg.company_records` row with full `raw_detail_payload`.

This validates the most important unknowns before we commit to a large import design.




