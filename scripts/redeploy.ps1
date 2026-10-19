#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Rebuilds the MatDo container and starts the local stack (docker-compose.local.yml).
.DESCRIPTION
    The data (database, keys) stays in the volumes; -Clean removes them first (empty database: the first account becomes the administrator again).
.EXAMPLE
    ./scripts/redeploy.ps1
.EXAMPLE
    ./scripts/redeploy.ps1 -Clean
#>
param([switch]$Clean)

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Windows PowerShell 5.1 turns the progress lines docker writes to stderr into errors: judge by the exit code.
    $ErrorActionPreference = 'Continue'
    $compose = @('compose', '-f', 'docker-compose.local.yml')
    if ($Clean) {
        docker @compose down -v --remove-orphans
    }

    docker @compose up -d --build --remove-orphans
    if ($LASTEXITCODE -ne 0) { throw 'docker compose ist fehlgeschlagen.' }

    Write-Host ''
    Write-Host 'MatDo laeuft auf http://localhost:6006 (pgAdmin: http://localhost:6007)' -ForegroundColor Green
}
finally {
    Pop-Location
}
