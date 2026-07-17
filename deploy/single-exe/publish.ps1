<#
.SYNOPSIS
  Build the Vue SPA and publish Factarium as a self-contained single-file executable.

.EXAMPLE
  ./deploy/single-exe/publish.ps1                 # win-x64
  ./deploy/single-exe/publish.ps1 -Runtime linux-x64
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/single-exe"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/../.."
Push-Location $root
try {
    Write-Host "==> Building SPA" -ForegroundColor Cyan
    Push-Location web
    npm ci
    npm run build
    Pop-Location

    Write-Host "==> Publishing single-file self-contained exe ($Runtime)" -ForegroundColor Cyan
    dotnet publish src/Factarium.Api/Factarium.Api.csproj `
        -c $Configuration -r $Runtime --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $Output

    Write-Host "==> Done -> $Output" -ForegroundColor Green
}
finally {
    Pop-Location
}
