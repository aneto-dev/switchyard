$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

Write-Host "Removing local Switchyard PostgreSQL data..." -ForegroundColor Yellow
docker compose -f infrastructure/local/compose.yml down -v
Write-Host "Local data removed." -ForegroundColor Green
