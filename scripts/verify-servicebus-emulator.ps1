[CmdletBinding()]
param(
    [Parameter(Mandatory)][switch]$AcceptEula,
    [ValidateRange(1, 10)][int]$Runs = 1,
    [ValidateRange(1, 65535)][int]$ServiceBusPort = 5673,
    [ValidateRange(1, 65535)][int]$HealthPort = 5301,
    [ValidateRange(30, 600)][int]$StartupTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = "infrastructure/local/servicebus-emulator.compose.yml"
$ComposeProject = "switchyard-servicebus-e2e"
$ConnectionStringVariable =
    "SWITCHYARD_SERVICEBUS_E2E_CONNECTION_STRING"

Set-Location -LiteralPath $RepoRoot

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

function Assert-PortAvailable {
    param(
        [Parameter(Mandatory)][int]$Port
    )

    $listener =
        [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            $Port)

    try {
        $listener.Start()
    }
    catch {
        throw "TCP port $Port is already in use. Choose another port."
    }
    finally {
        try {
            $listener.Stop()
        }
        catch {
        }
    }
}

function Wait-ServiceBusHealthy {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $deadline =
        (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        try {
            $response =
                Invoke-WebRequest `
                    -Uri "http://localhost:$Port/health" `
                    -UseBasicParsing `
                    -TimeoutSec 5

            if ($response.StatusCode -eq 200) {
                Write-Host "Service Bus emulator healthcheck passed." -ForegroundColor Green
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 3
    }
    while ((Get-Date) -lt $deadline)

    throw "Service Bus emulator did not become healthy within $TimeoutSeconds seconds."
}

if (-not $AcceptEula) {
    throw "Pass -AcceptEula only after accepting the Microsoft Service Bus emulator and SQL Server container licence terms."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI was not found."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found."
}

Invoke-Checked -Command "docker" -Arguments @(
    "version",
    "--format",
    "{{.Server.Version}}"
)

Assert-PortAvailable -Port $ServiceBusPort
Assert-PortAvailable -Port $HealthPort

$savedAcceptEula = $env:SWITCHYARD_SERVICEBUS_ACCEPT_EULA
$savedSqlPassword = $env:SWITCHYARD_SERVICEBUS_SQL_PASSWORD
$savedServiceBusPort = $env:SWITCHYARD_SERVICEBUS_PORT
$savedHttpPort = $env:SWITCHYARD_SERVICEBUS_HTTP_PORT
$savedConnectionString =
    [Environment]::GetEnvironmentVariable(
        $ConnectionStringVariable)

$env:SWITCHYARD_SERVICEBUS_ACCEPT_EULA = "Y"
$env:SWITCHYARD_SERVICEBUS_SQL_PASSWORD =
    "Sw!9$([guid]::NewGuid().ToString('N'))"
$env:SWITCHYARD_SERVICEBUS_PORT =
    $ServiceBusPort.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture)
$env:SWITCHYARD_SERVICEBUS_HTTP_PORT =
    $HealthPort.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture)

[Environment]::SetEnvironmentVariable(
    $ConnectionStringVariable,
    "Endpoint=sb://localhost:$ServiceBusPort;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;")

$composeArguments = @(
    "compose",
    "-p",
    $ComposeProject,
    "-f",
    $ComposeFile
)

try {
    Write-Host "`n=== SERVICE BUS EMULATOR STARTUP ===" -ForegroundColor Cyan
    Write-Host "Data port   : $ServiceBusPort"
    Write-Host "Health port : $HealthPort"
    Write-Host "EULA acceptance is process-scoped for this run." -ForegroundColor Yellow

    & docker @composeArguments down -v --remove-orphans *> $null

    Invoke-Checked -Command "docker" -Arguments (
        $composeArguments +
        @(
            "up",
            "-d"
        ))

    try {
        Wait-ServiceBusHealthy `
            -Port $HealthPort `
            -TimeoutSeconds $StartupTimeoutSeconds
    }
    catch {
        & docker @composeArguments ps
        & docker @composeArguments logs --no-color
        throw
    }

    Invoke-Checked -Command "docker" -Arguments (
        $composeArguments +
        @(
            "ps"
        ))

    Write-Host "`n=== REAL SERVICE BUS BROKER E2E ===" -ForegroundColor Cyan

    Invoke-Checked -Command "dotnet" -Arguments @(
        "restore",
        "tests/Switchyard.IntegrationTests/Switchyard.IntegrationTests.csproj"
    )

    Invoke-Checked -Command "dotnet" -Arguments @(
        "build",
        "tests/Switchyard.IntegrationTests/Switchyard.IntegrationTests.csproj",
        "-c",
        "Release",
        "--no-restore"
    )

    for ($run = 1; $run -le $Runs; $run++) {
        Write-Host "`nBroker E2E run $run of $Runs..." -ForegroundColor Yellow

        Invoke-Checked -Command "dotnet" -Arguments @(
            "test",
            "tests/Switchyard.IntegrationTests/Switchyard.IntegrationTests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--filter",
            "Category=ServiceBusEmulator"
        )
    }

    Write-Host "`nReal Service Bus broker E2E passed." -ForegroundColor Green
}
catch {
    Write-Host "`n=== SERVICE BUS EMULATOR FAILURE LOGS ===" -ForegroundColor Red

    try {
        & docker @composeArguments ps
        & docker @composeArguments logs --no-color
    }
    catch {
    }

    throw
}
finally {
    Write-Host "`n=== SERVICE BUS EMULATOR CLEANUP ===" -ForegroundColor Cyan

    try {
        & docker @composeArguments down -v --remove-orphans
    }
    catch {
        Write-Warning "Service Bus emulator cleanup encountered an error."
    }

    if ($null -eq $savedAcceptEula) {
        Remove-Item Env:SWITCHYARD_SERVICEBUS_ACCEPT_EULA -ErrorAction SilentlyContinue
    }
    else {
        $env:SWITCHYARD_SERVICEBUS_ACCEPT_EULA = $savedAcceptEula
    }

    if ($null -eq $savedSqlPassword) {
        Remove-Item Env:SWITCHYARD_SERVICEBUS_SQL_PASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:SWITCHYARD_SERVICEBUS_SQL_PASSWORD = $savedSqlPassword
    }

    if ($null -eq $savedServiceBusPort) {
        Remove-Item Env:SWITCHYARD_SERVICEBUS_PORT -ErrorAction SilentlyContinue
    }
    else {
        $env:SWITCHYARD_SERVICEBUS_PORT = $savedServiceBusPort
    }

    if ($null -eq $savedHttpPort) {
        Remove-Item Env:SWITCHYARD_SERVICEBUS_HTTP_PORT -ErrorAction SilentlyContinue
    }
    else {
        $env:SWITCHYARD_SERVICEBUS_HTTP_PORT = $savedHttpPort
    }

    [Environment]::SetEnvironmentVariable(
        $ConnectionStringVariable,
        $savedConnectionString)
}
