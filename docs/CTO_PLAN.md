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

- `GET /firmy` returns a paginated list of companies matching filters such as NIP, REGON, owner name, company name, address, PKD, date range, and status.
- `GET /firma` or `GET /firma/{id}` returns detailed company data.
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

Use a two-layer data model:

1. Raw source layer:
   - Store the exact JSON/XML payload from CEIDG.
   - Store endpoint, request parameters, response status, fetch timestamp, source schema/version, and content hash.
   - This protects us from schema drift and lets us reprocess without re-calling CEIDG.

2. Normalized relational layer:
   - Company core table.
   - Owner/person table where legally and technically appropriate.
   - Addresses table.
   - PKD codes table.
   - Status/history table.
   - Civil partnerships table.
   - Legal restrictions, licenses, qualifications, insolvency/succession sections if present in API v2 detailed payload.

The ingestion service writes raw payloads first, then transforms them into normalized tables in one transactional unit. A failed transform must not lose raw data.

## 4. Proposed .NET Solution Layout

Create one solution with clear service boundaries:

- `CeidgMirror.Api` - public/internal API over PostgreSQL.
- `CeidgMirror.Worker` - background ingestion and synchronization service.
- `CeidgMirror.Infrastructure` - PostgreSQL, HTTP clients, rate limiting, persistence.
- `CeidgMirror.Domain` - normalized domain model and business rules.
- `CeidgMirror.Application` - import orchestration, commands, queries, validation.
- `CeidgMirror.Contracts` - DTOs for API responses and CEIDG source models.
- `CeidgMirror.Tests` - unit and integration tests.

Recommended stack:

- .NET 9 or latest LTS available in the target deployment environment.
- ASP.NET Core Minimal APIs or Controllers.
- EF Core for normalized relational model.
- Dapper or Npgsql bulk APIs for high-volume import paths where EF is too slow.
- PostgreSQL with JSONB for raw payloads and GIN indexes for selective raw queries.
- Hangfire, Quartz.NET, or a hosted worker with durable import job tables.
- OpenTelemetry, Serilog, and structured metrics from day one.

## 5. PostgreSQL Schema Strategy

Initial schemas:

- `source` for raw CEIDG responses and import metadata.
- `ceidg` for normalized current-state tables.
- `history` for change tracking and slowly changing dimensions.
- `app` for API users, keys, saved searches, exports, and product-specific data later.

Critical tables:

- `source.api_request_log`
- `source.raw_company_payload`
- `source.raw_report_payload`
- `source.import_checkpoint`
- `ceidg.company`
- `ceidg.company_identifier`
- `ceidg.address`
- `ceidg.company_address`
- `ceidg.pkd_code`
- `ceidg.company_pkd`
- `ceidg.status_history`
- `ceidg.change_event`

Identity and idempotency:

- Use CEIDG `id` as the main external identifier when available.
- Keep NIP and REGON as indexed identifiers, not as the only primary keys.
- Use a payload hash to skip unchanged records.
- Store every import run and checkpoint so re-runs are safe.

## 6. Import Strategy

Phase 1: API access and schema discovery

- Obtain/confirm JWT credentials for test and production.
- Generate or hand-write typed clients from observed API v2 JSON.
- Fetch sample `/firmy`, `/firma/{id}`, `/raporty`, `/raport/{id}`, and `/zmiana` responses.
- Save golden sample payloads under tests.

Phase 2: Full initial load

- Prefer official reports from `/raporty` and `/raport/{id}` if they provide bulk/full datasets.
- If reports are not suitable, partition `/firmy` requests by deterministic criteria:
  - date ranges,
  - statuses,
  - possibly PKD/address partitions only if needed.
- For each listed company id, fetch detailed `/firma/{id}`.
- Run with a global distributed rate limiter set below the documented API ceilings.

Phase 3: Incremental synchronization

- Use `/zmiana?dataod=YYYY-MM-DD&datado=YYYY-MM-DD&page=...&limit=...`.
- For every changed identifier, refetch detailed company data.
- Upsert normalized tables and append a `ceidg.change_event`.
- Maintain checkpoints by date window and page.
- Reconcile daily, weekly, and monthly to catch missed changes.

Phase 4: Reprocessing and enrichment

- Re-run transformations from raw payloads after schema/model changes.
- Add analytics projections and materialized views.
- Add search indexes and API-specific denormalized read models.

## 7. API Product Roadmap

MVP endpoints:

- Search companies by NIP, REGON, name, status, PKD, city, voivodeship, and date ranges.
- Company detail endpoint with normalized CEIDG data.
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
- Add raw payload tables.
- Add import run/checkpoint tables.
- Add first normalized `company`, `address`, and `pkd` tables.

Milestone 3: Initial importer

- Implement paginated `/firmy` import.
- Implement detail hydration through `/firma/{id}`.
- Persist raw and normalized records transactionally.
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
4. Fetch one page from `/firmy` and one detail record from `/firma/{id}` in the test environment.
5. Persist both raw JSON and the first normalized company row.

This validates the most important unknowns before we commit to a large import design.
