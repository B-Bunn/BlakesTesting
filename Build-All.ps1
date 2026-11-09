<#
.SYNOPSIS
    Builds BlakesTesting.dll for every Revit year (2023-2027), one after another, and prints a summary table.

.DESCRIPTION
    Years share one obj\ folder, so they are built in order, never at once. Output goes to
    src\BlakesTesting\bin\<Debug|Release>\<year>\ (the DLL and BlakesTesting.addin). R27 needs the .NET 10 SDK and is
    reported as skipped, not failed, without it.

.PARAMETER Configuration
    Debug or Release. Debug by default (this is a testing repo).

.PARAMETER Dist
    After a successful build, copy the DLL, PDB and .addin of each year into dist\<year>\ (the folder committed to the
    repo so the DLL can be loaded without building). Use with -Configuration Release.

.PARAMETER Only
    Comma-separated Revit years, e.g. "2023" or "23,25". All five when omitted.

.EXAMPLE
    .\Build-All.ps1
    .\Build-All.ps1 -Only 2023
    .\Build-All.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Only,
    [switch]$Dist
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\BlakesTesting\BlakesTesting.csproj'

$years = 2023, 2024, 2025, 2026, 2027
if ($Only) {
    $years = $Only -split ',' | ForEach-Object { $y = [int]($_.Trim().TrimStart('R', 'r')); if ($y -lt 100) { 2000 + $y } else { $y } }
}

$sdkMajor = [int]((dotnet --version) -split '\.')[0]
$results = @()
foreach ($year in $years) {
    $config = "$Configuration R$($year % 100)"
    if ($year -eq 2027 -and $sdkMajor -lt 10) {
        Write-Host "$config  skipped - needs the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10)"
        $results += [pscustomobject]@{ Year = $year; Configuration = $config; Result = 'skipped'; Seconds = '' }
        continue
    }

    Write-Host "== $config"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    dotnet build $project -c $config --nologo -v:q
    $ok = ($LASTEXITCODE -eq 0)
    if ($ok -and $Dist) {
        $out = Join-Path $PSScriptRoot "dist\$year"
        New-Item -ItemType Directory -Force $out | Out-Null
        Copy-Item (Join-Path $PSScriptRoot "src\BlakesTesting\bin\$Configuration\$year\BlakesTesting.*") $out -Include *.dll, *.pdb, *.addin
    }
    $results += [pscustomobject]@{ Year = $year; Configuration = $config; Result = $(if ($ok) { 'ok' } else { 'FAILED' }); Seconds = [int]$watch.Elapsed.TotalSeconds }
}

$results | Format-Table -AutoSize
if ($results.Result -contains 'FAILED') { exit 1 }
