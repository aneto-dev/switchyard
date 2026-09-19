[CmdletBinding()]
param(
    [string]$ConnectionString = $env:SWITCHYARD_PAYMENTS_CONNECTION_STRING
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

if ([string]::IsNullOrWhiteSpace($ConnectionString))
{
    $ConnectionString = "Host=localhost;Port=5432;Database=switchyard;Username=switchyard;Password=switchyard-local-only"
}

$env:SWITCHYARD_PAYMENTS_CONNECTION_STRING = $ConnectionString

dotnet tool restore

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet tool restore failed with exit code $LASTEXITCODE."
}

dotnet tool run dotnet-ef database update `
    --project src/Switchyard.Payments.Infrastructure/Switchyard.Payments.Infrastructure.csproj `
    --startup-project src/Switchyard.Api/Switchyard.Api.csproj

if ($LASTEXITCODE -ne 0)
{
    throw "Payments migration failed with exit code $LASTEXITCODE."
}
