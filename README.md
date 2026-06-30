# CEIDG_API_WRAPER

C#/.NET solution for mirroring CEIDG company data into PostgreSQL and exposing a paid/token-billed API over the local mirror.

## Projects

- `CeidgMirror.Worker` - imports CEIDG company ids from `/zmiana`, fetches details from `/firma/{id}`, and upserts rows into PostgreSQL.
- `CeidgMirror.Api` - Swagger/API host for authenticated users querying the local CEIDG mirror.
- `CeidgMirror.Infrastructure` - CEIDG HTTP clients, PostgreSQL stores, parsers, request pacing.
- `CeidgMirror.Application`, `CeidgMirror.Contracts`, `CeidgMirror.Domain` - shared contracts and application abstractions.
- `CeidgMirror.Tests` - unit tests.

## Database Files

- `db/init/001_schema.sql` - full schema for a new Docker PostgreSQL volume. Docker runs it automatically only on first database initialization.
- `db/setup_local_database.sql` - creates/updates the local Windows PostgreSQL database `ceidg_mirror`.
- `db/migrations/*.sql` - incremental SQL migrations for existing databases.

Current schemas/tables:

- `ceidg.company_records` - one main row per CEIDG company plus raw JSON payloads.
- `source.import_run`, `source.import_checkpoint` - import run audit and resumable worker state.
- `source.report_payload`, `source.report_company_link` - optional report repository data.
- `app.api_users`, `app.api_keys`, `app.token_ledger`, `app.api_query_log` - API users, API keys and token billing.

## Build And Test

```powershell
dotnet build CeidgMirror.slnx
dotnet test CeidgMirror.slnx --no-build
```

## Windows Local Database

For your local PostgreSQL 18 on port `5433`:

```powershell
$env:PGPASSWORD = "postgres"
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -p 5433 -U postgres -d postgres -v ON_ERROR_STOP=1 -f db\setup_local_database.sql
```

Run a new migration manually, for example:

```powershell
$env:PGPASSWORD = "postgres"
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -p 5433 -U postgres -d ceidg_mirror -v ON_ERROR_STOP=1 -f db\migrations\20260630_001_product_api.sql
```

All schema SQL uses `create ... if not exists`, so it is safe to rerun.

## Worker Locally

Use `CeidgMirror.Worker` as the startup project in Visual Studio, not `CeidgMirror.Api`.

PowerShell example:

```powershell
$env:CeidgApi__JwtToken = "YOUR_CEIDG_TOKEN"
$env:CeidgApi__BaseUrl = "https://dane.biznes.gov.pl/api/ceidg/v3/"
$env:Postgres__ConnectionString = "Host=localhost;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres"
$env:Import__Enabled = "true"
$env:Import__RunOnce = "false"
$env:Import__Source = "ChangesApi"
$env:Import__ChangesFrom = "2011-07-01"
$env:Import__ChangesWindowDays = "1"
$env:Import__MaxPages = "0"
$env:Import__PageLimit = "50"
$env:Import__MaxCompanies = "0"
dotnet run --project src\CeidgMirror.Worker\CeidgMirror.Worker.csproj
```

The worker is resumable. Progress is persisted in `source.import_checkpoint` after each processed company. Keep `MaxPages=0` and `MaxCompanies=0` for a full mirror.

Request pacing:

- 50 requests / 180 seconds.
- 1000 requests / 3600 seconds.
- Smooth interval: `CeidgApi__MinimumRequestIntervalSeconds=4`.

## API Locally

Use `CeidgMirror.Api` as the startup project.

Swagger UI:

```text
http://localhost:5075/swagger
```

Core endpoints:

- `POST /auth/register` - creates a user, grants free starting tokens, returns the first API key.
- `POST /auth/login` - verifies email/password and issues a new API key.
- `GET /account/balance` - requires `X-Api-Key`.
- `GET /billing/token-packages` - lists token packages for future payment integration.
- `GET /companies/columns` - lists selectable columns and token weights.
- `GET /companies` - paginated company search with dynamic columns; requires `X-Api-Key`.

Example:

```powershell
$headers = @{ "X-Api-Key" = "YOUR_API_KEY" }
Invoke-RestMethod -Headers $headers "http://localhost:5075/companies?page=1&pageSize=25&columns=ceidgId,nip,name,city,email,pkdCodes&city=Warszawa"
```

Token cost depends on selected column weights and returned row count. Insufficient balance returns HTTP `402 Payment Required`.

## Docker On Server

Set secrets in the shell or in an untracked `.env` file. Do not commit real tokens. The worker will start without `CEIDG_JWT_TOKEN`, but CEIDG requests will return `401`; check the startup log for `HasJwtToken=True`.

Example `.env` on the server:

```env
CEIDG_JWT_TOKEN=YOUR_CEIDG_TOKEN
POSTGRES_CONNECTION_STRING=Host=postgres;Port=5432;Database=ceidg_mirror;Username=ceidg;Password=ceidg_dev_password
```

Start PostgreSQL only:

```bash
docker compose up -d postgres
```

Build images without cache after Dockerfile changes:

```bash
docker compose build --no-cache worker api
```

Run worker:

```bash
docker compose --profile worker up -d worker
docker logs -f ceidg-mirror-worker
```

Run API/Swagger:

```bash
docker compose --profile api up -d api
docker logs -f ceidg-mirror-api
```

Swagger is exposed on the host at:

```text
http://SERVER_IP:5075/swagger
```

Run PostgreSQL + worker + API together:

```bash
docker compose --profile worker --profile api up -d --build
```

If a containerized service should use PostgreSQL installed directly on the host instead of Compose PostgreSQL, set:

```bash
export POSTGRES_CONNECTION_STRING='Host=host.docker.internal;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres'
```

On Linux, `host.docker.internal` may require Docker's host-gateway configuration. The default Compose setup is simpler: use `Host=postgres` and the bundled `postgres` service.

## Server Troubleshooting

### CEIDG returns 401 Unauthorized

Check worker logs:

```bash
docker logs ceidg-mirror-worker | grep HasJwtToken
```

Expected:

```text
HasJwtToken=True, JwtTokenLength=...
```

If `HasJwtToken=False`, the container did not receive `CEIDG_JWT_TOKEN`. If it is `True` but CEIDG still returns `401`, the token is invalid, expired, truncated, or contains accidental spaces/newlines.

### Missing ASP.NET Docker tag during API build

If Docker reports that `mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim` is not found, pull the latest repo version. `Dockerfile.api` intentionally uses `mcr.microsoft.com/dotnet/sdk:10.0` as the final image because it is available for this project target and includes the ASP.NET runtime needed by the API.

### Missing libgssapi_krb5.so.2

The worker/API Dockerfiles install `libgssapi-krb5-2`. Rebuild without cache:

```bash
docker compose build --no-cache worker api
```

### SQL init did not run in Docker

`db/init/001_schema.sql` runs only when PostgreSQL initializes an empty volume. If the volume already exists, run migrations manually or recreate the volume intentionally.

Manual migration inside Compose PostgreSQL:

```bash
docker compose exec -T postgres psql -U ceidg -d ceidg_mirror < db/migrations/20260630_001_product_api.sql
```

## Security Notes

- Do not commit CEIDG JWTs, API keys, user passwords or `.env` files.
- `appsettings.Local.json`, `.env`, `.env.*` and `secrets.json` are ignored by git.
- User API keys are stored as SHA-256 hashes in `app.api_keys`.
- User passwords are stored as PBKDF2-SHA256 hashes.

## Documents

- [CTO plan](docs/CTO_PLAN.md)
- [CEIDG field coverage audit](docs/CEIDG_FIELD_AUDIT.md)


