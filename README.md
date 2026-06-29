# CEIDG_API_WRAPER

Private C#/.NET project for building a local PostgreSQL analytical mirror of CEIDG data.

The target product is a data platform/API that can ingest CEIDG public business data, preserve raw source payloads, normalize the most important entities into relational tables, and expose clean query/analysis endpoints for downstream services.

## Current Status

The repository contains the CTO plan, CEIDG source documentation copies, and the first .NET solution skeleton:

- `CeidgMirror.Api` - API host for read/query endpoints.
- `CeidgMirror.Worker` - background ingestion/synchronization host.
- `CeidgMirror.Application` - orchestration interfaces.
- `CeidgMirror.Infrastructure` - CEIDG HTTP client, report repository client, and rate pacing.
- `CeidgMirror.Contracts` - source/API DTO contracts.
- `CeidgMirror.Domain` - domain model.
- `CeidgMirror.Tests` - unit tests.

## Source APIs

- Active CEIDG company API: `https://dane.biznes.gov.pl/api/ceidg/v3`
- CEIDG report repository: `https://dane.biznes.gov.pl/raporty/`
- Older CEIDG API v2 documentation remains in the repo, but the former `https://dane.biznes.gov.pl/api/ceidg/v2` context currently returns `404 No context-path matches the request URI`.
- Legacy CEIDG DataStore SOAP API: documented in `OpisAPINew.pdf`.

## Key Constraints

- CEIDG company API uses JWT bearer authentication in the `Authorization` header.
- CEIDG enforces request limits: 50 requests per 3 minutes and 1000 requests per 60 minutes.
- The primary company import flow is `/zmiana` -> `/firma/{id}` so each discovered company is hydrated with a full detail payload.
- Reports are an auxiliary source and are stored separately in `source.report_payload`.
- The local PostgreSQL database must retain source fidelity, not only a lossy projection.

## Build And Test

```powershell
dotnet build CeidgMirror.slnx
dotnet test CeidgMirror.slnx --no-build
```

## Local PostgreSQL

```powershell
docker compose up -d postgres
```

The development database is created as `ceidg_mirror` with initial schemas from `db/init/001_schema.sql`.

For a local PostgreSQL instance that already exists on Windows, run:

```powershell
$env:PGPASSWORD = "postgres"
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -p 5433 -U postgres -d postgres -v ON_ERROR_STOP=1 -f db\setup_local_database.sql
```

## Import Worker

The worker does not import automatically by default. Enable it explicitly:

```powershell
$env:CeidgApi__JwtToken = "YOUR_TOKEN"
$env:CeidgApi__BaseUrl = "https://dane.biznes.gov.pl/api/ceidg/v3/"
$env:Import__Enabled = "true"
$env:Import__RunOnce = "true"
$env:Import__Source = "ChangesApi"
$env:Import__ChangesFrom = "2026-06-28"
$env:Import__MaxPages = "1"
$env:Import__PageLimit = "1"
$env:Import__MaxCompanies = "1"
$env:Postgres__ConnectionString = "Host=localhost;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres"
dotnet run --project src\CeidgMirror.Worker\CeidgMirror.Worker.csproj
```

The primary importer reads company ids from `/zmiana`, then fetches full company details from `/firma/{id}`, and finally upserts one row into `ceidg.company_records` with the full `raw_detail_payload`. The older `/firmy` index flow remains available as `Import__Source=RestApi`, but the live production service currently works through the v3 change/detail flow.

API pacing is enforced with both CEIDG documented windows: 50 requests per 180 seconds and 1000 requests per 3600 seconds.

## CEIDG Configuration

Set the JWT outside source control, for example:

```powershell
$env:CeidgApi__JwtToken = "YOUR_TOKEN"
```

Do not commit real tokens. The current default base URL points to the active CEIDG v3 company API.

## Documents

- [CTO plan](docs/CTO_PLAN.md)
- [CEIDG field coverage audit](docs/CEIDG_FIELD_AUDIT.md)