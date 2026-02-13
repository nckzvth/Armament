$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$AnimRoot = Join-Path $RootDir 'content/animations'
$ArtifactRoot = Join-Path $RootDir '.artifacts'

$Type = 'class'
$Id = ''
$RunEdit = $true
$RunValidate = $true
$FailOnError = $true

for ($i = 0; $i -lt $args.Length; $i++) {
    switch ($args[$i]) {
        '--type' {
            $Type = $args[++$i].ToLowerInvariant()
        }
        '--id' {
            $Id = $args[++$i]
        }
        '--edit-only' {
            $RunEdit = $true
            $RunValidate = $false
        }
        '--validate-only' {
            $RunEdit = $false
            $RunValidate = $true
        }
        '--no-fail' {
            $FailOnError = $false
        }
        '--help' {
            Write-Host "Usage: import-animation-assets.ps1 [--type class|enemy|npc|prop|all] [--id <id>] [--edit-only|--validate-only] [--no-fail]"
            exit 0
        }
        default {
            throw "Unknown arg: $($args[$i])"
        }
    }
}

if ($Type -notin @('class', 'enemy', 'npc', 'prop', 'all')) {
    throw "Invalid --type '$Type'. Expected class|enemy|npc|prop|all."
}

if ($Type -ne 'all' -and [string]::IsNullOrWhiteSpace($Id)) {
    throw "--id is required unless --type all is used."
}

function Resolve-ScopeDir([string]$scopeType, [string]$scopeId) {
    if ($scopeType -eq 'all') { return $AnimRoot }

    $candidates = @()
    switch ($scopeType) {
        'class' {
            $candidates += (Join-Path $AnimRoot $scopeId)
            $candidates += (Join-Path $AnimRoot "classes/$scopeId")
            $candidates += (Join-Path $AnimRoot "characters/$scopeId")
        }
        'enemy' { $candidates += (Join-Path $AnimRoot "enemies/$scopeId") }
        'npc' { $candidates += (Join-Path $AnimRoot "npcs/$scopeId") }
        'prop' { $candidates += (Join-Path $AnimRoot "props/$scopeId") }
    }

    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c -PathType Container) { return $c }
    }

    if ($scopeType -eq 'class') {
        return (Join-Path $AnimRoot $scopeId)
    }

    return (Join-Path $AnimRoot "$($scopeType)s/$scopeId")
}

$ScopeDir = Resolve-ScopeDir $Type $Id
New-Item -ItemType Directory -Force -Path $ScopeDir | Out-Null
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null

$ScopeSlug = if ([string]::IsNullOrWhiteSpace($Id)) { $Type } else { "$Type.$Id" }
$ReportOut = Join-Path $ArtifactRoot "atlas-validation-report.$ScopeSlug.txt"
$CatalogOut = Join-Path $ArtifactRoot "atlas-catalog.$ScopeSlug.json"
$OverlayDir = Join-Path $ArtifactRoot "atlas-overlays/$ScopeSlug"
New-Item -ItemType Directory -Force -Path $OverlayDir | Out-Null

Push-Location $RootDir
try {
    Write-Host "[import-animation] root: $RootDir"
    Write-Host "[import-animation] scope: $ScopeDir"

    if ($RunEdit) {
        Write-Host "[import-animation] opening Atlas Editor..."
        dotnet run --project (Join-Path $RootDir 'ops/tools/AtlasEditor/Armament.AtlasEditor.csproj') -- --input-dir $ScopeDir
    }

    if ($RunValidate) {
        Write-Host "[import-animation] running AtlasValidator..."
        $validateArgs = @(
            '--input-dir', $ScopeDir,
            '--report-out', $ReportOut,
            '--catalog-out', $CatalogOut,
            '--overlay-dir', $OverlayDir
        )
        if (Test-Path -LiteralPath (Join-Path $AnimRoot 'clipmaps') -PathType Container) {
            $validateArgs += @('--clipmap-dir', (Join-Path $AnimRoot 'clipmaps'))
        }
        if ($FailOnError) { $validateArgs += '--fail-on-error' }

        dotnet run --project (Join-Path $RootDir 'ops/tools/AtlasValidator') -- @validateArgs
        Write-Host "[import-animation] report: $ReportOut"
        Write-Host "[import-animation] catalog: $CatalogOut"
        Write-Host "[import-animation] overlays: $OverlayDir"
    }
}
finally {
    Pop-Location
}

Write-Host "[import-animation] done."
