# CEIDG_API_WRAPER

Private C#/.NET project for building a local PostgreSQL analytical mirror of CEIDG data.

The target product is a data platform/API that can ingest CEIDG public business data, preserve raw source payloads, normalize the most important entities into relational tables, and expose clean query/analysis endpoints for downstream services.

## Current Status

The repository contains the CTO plan, CEIDG source documentation copies, and the first .NET solution skeleton:

- `CeidgMirror.Api` - API host for read/query endpoints.
- `CeidgMirror.Worker` - background ingestion/synchronization host.
- `CeidgMirror.Application` - orchestration interfaces.
- `CeidgMirror.Infrastructure` - CEIDG HTTP client and rate pacing.
- `CeidgMirror.Contracts` - source/API DTO contracts.
- `CeidgMirror.Domain` - domain model.
- `CeidgMirror.Tests` - unit tests.

## Source APIs

- CEIDG API v2 Hurtowni Danych: `https://dane.biznes.gov.pl/api/ceidg/v2`
- Test CEIDG API v2: `https://test-dane.biznes.gov.pl/api/ceidg/v2`
- Legacy CEIDG DataStore SOAP API: documented in `OpisAPINew.pdf`

## Key Constraints

- API v2 uses JWT bearer authentication in the `Authorization` header.
- API v2 enforces request limits: 50 requests per 3 minutes and 1000 requests per 60 minutes.
- API v2 exposes paginated list endpoints and a change endpoint for incremental synchronization.
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
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h localhost -p 5432 -U postgres -d postgres -v ON_ERROR_STOP=1 -f db\setup_local_database.sql
```

Use `-p 5433` for the second local PostgreSQL instance if needed.

## CEIDG Configuration

Set the JWT outside source control, for example:

```powershell
$env:CeidgApi__JwtToken = "YOUR_TOKEN"
```

The current default base URL points to the CEIDG test environment.

## Documents

- [CTO plan](docs/CTO_PLAN.md)
- [CEIDG field coverage audit](docs/CEIDG_FIELD_AUDIT.md)





