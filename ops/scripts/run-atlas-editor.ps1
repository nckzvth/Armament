$ErrorActionPreference = 'Stop'
$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$InputDir = if ($args.Length -gt 0) { $args[0] } else { Join-Path $RootDir 'content/animations' }

Push-Location $RootDir
try {
    dotnet run --project (Join-Path $RootDir 'ops/tools/AtlasEditor/Armament.AtlasEditor.csproj') -- --input-dir $InputDir
}
finally {
    Pop-Location
}
