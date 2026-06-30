param(
    [string]$HostName = "localhost",
    [int]$Port = 5433,
    [string]$Database = "ceidg_mirror",
    [string]$Username = "postgres",
    [string]$Password = "postgres",
    [string]$PsqlPath = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$migrationsPath = Join-Path $repoRoot "db\migrations"

if (-not (Test-Path -LiteralPath $PsqlPath)) {
    throw "psql.exe not found at: $PsqlPath"
}

if (-not (Test-Path -LiteralPath $migrationsPath)) {
    throw "Migrations directory not found: $migrationsPath"
}

$migrations = Get-ChildItem -LiteralPath $migrationsPath -Filter "*.sql" | Sort-Object Name
if ($migrations.Count -eq 0) {
    Write-Host "No SQL migrations found in $migrationsPath"
    exit 0
}

$previousPassword = $env:PGPASSWORD
$env:PGPASSWORD = $Password

try {
    foreach ($migration in $migrations) {
        Write-Host "Running migration: $($migration.Name)"
        & $PsqlPath `
            -h $HostName `
            -p $Port `
            -U $Username `
            -d $Database `
            -v ON_ERROR_STOP=1 `
            -f $migration.FullName

        if ($LASTEXITCODE -ne 0) {
            throw "Migration failed: $($migration.Name)"
        }
    }

    Write-Host "All migrations completed."
}
finally {
    if ($null -eq $previousPassword) {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:PGPASSWORD = $previousPassword
    }
}
