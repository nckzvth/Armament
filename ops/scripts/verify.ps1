$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:DOTNET_CLI_HOME = Join-Path $RootDir '.dotnet_home'

Push-Location $RootDir
try {
    if (Test-Path (Join-Path $RootDir 'client-unity')) {
        throw 'Legacy Unity client directory exists at client-unity. Migration expects MonoGame-only repo.'
    }

    if (Test-Path (Join-Path $RootDir 'ops/scripts/run-unity-tests.sh') -or
        Test-Path (Join-Path $RootDir 'ops/scripts/run-unity-tests.ps1')) {
        throw 'Legacy Unity runner scripts still exist in ops/scripts.'
    }

    dotnet --info
    dotnet format 'shared-sim/Armament.SharedSim.sln' --verify-no-changes
    dotnet format 'server-dotnet/Armament.Server.sln' --verify-no-changes
    dotnet format 'client-mg/Armament.Client.MonoGame.sln' --verify-no-changes

    dotnet run --project 'shared-sim/Tests/SharedSim.Tests/Armament.SharedSim.Tests.csproj'
    dotnet run --project 'shared-sim/Tests/ContentValidation.Tests/Armament.ContentValidation.Tests.csproj'
    dotnet run --project 'shared-sim/Tests/WorldContentValidation.Tests/Armament.WorldContentValidation.Tests.csproj'
    New-Item -ItemType Directory -Force -Path (Join-Path $RootDir '.artifacts') | Out-Null
    dotnet run --project 'ops/tools/AtlasValidator' -- --input-dir (Join-Path $RootDir 'content/animations') --fail-on-error --report-out (Join-Path $RootDir '.artifacts/atlas-validation-report.txt') --catalog-out (Join-Path $RootDir '.artifacts/atlas-catalog.json')
    dotnet run --project 'server-dotnet/Tests/GameServer.Tests/Armament.GameServer.Tests.csproj'
    dotnet run --project 'server-dotnet/Tests/Persistence.Tests/Armament.Persistence.Tests.csproj'
    dotnet build 'client-mg/Armament.Client.MonoGame/Armament.Client.MonoGame.csproj'
    dotnet test 'client-mg/Tests/Armament.Client.MonoGame.Tests/Armament.Client.MonoGame.Tests.csproj'
    dotnet build 'ops/tools/AtlasEditor/Armament.AtlasEditor.csproj'
}
finally {
    Pop-Location
}
