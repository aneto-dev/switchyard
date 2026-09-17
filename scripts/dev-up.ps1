$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

docker compose -f infrastructure/local/compose.yml up -d postgres
docker compose -f infrastructure/local/compose.yml ps
