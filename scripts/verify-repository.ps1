[CmdletBinding()]
param(
    [switch]$SkipDockerComposeUp,
    [switch]$CleanupDockerCompose
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @()
    )

    & $Command @Arguments
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "'$Command $($Arguments -join ' ')' failed with exit code $exitCode."
    }
}

function Get-CheckedOutput {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @()
    )

    $output = & $Command @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        throw "'$Command $($Arguments -join ' ')' failed with exit code $exitCode.`n$message"
    }

    return (($output | Out-String).Trim())
}

function Wait-PostgreSqlHealthy {
    param([int]$TimeoutSeconds = 60)

    $containerId = Get-CheckedOutput -Command "docker" -Arguments @(
        "compose", "-f", "infrastructure/local/compose.yml", "ps", "-q", "postgres"
    )

    if ([string]::IsNullOrWhiteSpace($containerId)) {
        throw "PostgreSQL container was not created by Docker Compose."
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $health = Get-CheckedOutput -Command "docker" -Arguments @(
            "inspect", "--format", "{{.State.Health.Status}}", $containerId
        )

        if ($health -eq "healthy") {
            Write-Host "PostgreSQL healthcheck passed." -ForegroundColor Green
            return
        }

        if ($health -eq "unhealthy") {
            Invoke-Checked -Command "docker" -Arguments @(
                "compose", "-f", "infrastructure/local/compose.yml", "logs", "postgres"
            )
            throw "PostgreSQL became unhealthy."
        }

        Start-Sleep -Seconds 2
    }
    while ((Get-Date) -lt $deadline)

    Invoke-Checked -Command "docker" -Arguments @(
        "compose", "-f", "infrastructure/local/compose.yml", "logs", "postgres"
    )
    throw "PostgreSQL did not become healthy within $TimeoutSeconds seconds."
}

Assert-Command dotnet
Assert-Command node
Assert-Command npm
Assert-Command docker

$DotnetVersion = Get-CheckedOutput -Command "dotnet" -Arguments @("--version")
$NodeVersion = (Get-CheckedOutput -Command "node" -Arguments @("--version")).TrimStart('v')
$NpmVersion = Get-CheckedOutput -Command "npm" -Arguments @("--version")
$DockerClientVersion = Get-CheckedOutput -Command "docker" -Arguments @("--version")

if (-not $DotnetVersion.StartsWith("10.0.")) {
    throw "Switchyard requires .NET SDK 10.0.x. Found $DotnetVersion."
}

$NodeMajor = [int]($NodeVersion.Split('.')[0])
if ($NodeMajor -ne 24) {
    throw "Switchyard requires Node 24 LTS. Found $NodeVersion."
}

try {
    $DockerServerVersion = Get-CheckedOutput -Command "docker" -Arguments @("info", "--format", "{{.ServerVersion}}")
}
catch {
    throw "Docker CLI is installed but the Docker engine is not available. Start Docker Desktop and wait until it reports that the engine is running, then retry.`n$($_.Exception.Message)"
}

Write-Host "`n=== TOOLCHAIN ===" -ForegroundColor Cyan
Write-Host ".NET          : $DotnetVersion"
Write-Host "Node          : $NodeVersion"
Write-Host "npm           : $NpmVersion"
Write-Host "Docker client : $DockerClientVersion"
Write-Host "Docker engine : $DockerServerVersion"

if (-not (Test-Path package-lock.json)) {
    throw "package-lock.json is required for reproducible setup."
}

Write-Host "`n=== NPM CLEAN INSTALL ===" -ForegroundColor Cyan
Invoke-Checked -Command "npm" -Arguments @("ci")

Write-Host "`n=== DOTNET RESTORE / FORMAT / BUILD ===" -ForegroundColor Cyan
Invoke-Checked -Command "dotnet" -Arguments @("restore", "Switchyard.sln")
Invoke-Checked -Command "dotnet" -Arguments @("format", "Switchyard.sln", "--verify-no-changes", "--no-restore")
Invoke-Checked -Command "dotnet" -Arguments @("build", "Switchyard.sln", "-c", "Release", "--no-restore")

Write-Host "`n=== FRONTEND QUALITY ===" -ForegroundColor Cyan
Invoke-Checked -Command "npm" -Arguments @("run", "lint")
Invoke-Checked -Command "npm" -Arguments @("run", "typecheck")
Invoke-Checked -Command "npm" -Arguments @("run", "build:web")

Write-Host "`n=== DOCKER COMPOSE VALIDATION ===" -ForegroundColor Cyan
Invoke-Checked -Command "docker" -Arguments @("compose", "-f", "infrastructure/local/compose.yml", "config", "--quiet")

try {
    if (-not $SkipDockerComposeUp) {
        Invoke-Checked -Command "docker" -Arguments @("compose", "-f", "infrastructure/local/compose.yml", "up", "-d", "postgres")
        Wait-PostgreSqlHealthy
        Invoke-Checked -Command "docker" -Arguments @("compose", "-f", "infrastructure/local/compose.yml", "ps")
    }

    Write-Host "`n=== DOTNET TESTS ===" -ForegroundColor Cyan
    Invoke-Checked -Command "dotnet" -Arguments @("test", "Switchyard.sln", "-c", "Release", "--no-build")
}
finally {
    if ($CleanupDockerCompose -and -not $SkipDockerComposeUp) {
        Write-Host "`n=== DOCKER COMPOSE CLEANUP ===" -ForegroundColor Cyan
        Invoke-Checked -Command "docker" -Arguments @("compose", "-f", "infrastructure/local/compose.yml", "down", "-v")
    }
}

Write-Host "`nSwitchyard repository verification passed." -ForegroundColor Green
