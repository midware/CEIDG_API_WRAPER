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
$env:Import__RunOnce = "false"
$env:Import__Source = "ChangesApi"
$env:Import__ChangesFrom = "2011-07-01"
$env:Import__MaxPages = "0"
$env:Import__PageLimit = "50"
$env:Import__MaxCompanies = "0"
$env:Postgres__ConnectionString = "Host=localhost;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres"
dotnet run --project src\CeidgMirror.Worker\CeidgMirror.Worker.csproj
```

The primary importer reads company ids from `/zmiana`, then fetches full company details from `/firma/{id}`, and finally upserts one row into `ceidg.company_records` with the full `raw_detail_payload`. The older `/firmy` index flow remains available as `Import__Source=RestApi`, but the live production service currently works through the v3 change/detail flow.

For a full mirror, keep `MaxPages=0` and `MaxCompanies=0`. Progress is persisted in `source.import_checkpoint` after each processed company. If the worker is stopped or crashes, the next run resumes from `next_page` and `next_item_index` instead of starting from page 1. With `SkipExistingCompanies=true`, already imported CEIDG ids are skipped without calling `/firma/{id}` again.

Request pacing is centralized in `SlidingWindowRequestPacer`:

- CEIDG hard windows: 50 requests / 180 seconds and 1000 requests / 3600 seconds.
- Smooth pacing: `CeidgApi__MinimumRequestIntervalSeconds=4.0`, which keeps the worker below both limits instead of bursting requests.
- Slow historical pages are allowed by `CeidgApi__RequestTimeoutSeconds=300`.


## CEIDG Configuration

Set the JWT outside source control, for example:

```powershell
$env:CeidgApi__JwtToken = "YOUR_TOKEN"
```

Do not commit real tokens. The current default base URL points to the active CEIDG v3 company API.

## Docker Worker

Build the worker image:

```powershell
docker build -t ceidg-mirror-worker .
```

Run PostgreSQL and the import worker through Compose:

```powershell
$env:CEIDG_JWT_TOKEN = "YOUR_TOKEN"
docker compose --profile worker up -d --build
```

The Compose worker connects to the Compose PostgreSQL service with `Host=postgres;Port=5432`. If you want a containerized worker to use your Windows PostgreSQL on port `5433`, override:

```powershell
$env:Postgres__ConnectionString = "Host=host.docker.internal;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres"
docker compose --profile worker up -d --build worker
```
## Documents

- [CTO plan](docs/CTO_PLAN.md)
- [CEIDG field coverage audit](docs/CEIDG_FIELD_AUDIT.md)
