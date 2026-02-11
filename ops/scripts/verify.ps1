$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:DOTNET_CLI_HOME = Join-Path $RootDir '.dotnet_home'

Push-Location $RootDir
try {
    dotnet --info
    dotnet format 'shared-sim/Armament.SharedSim.sln' --verify-no-changes
    dotnet format 'server-dotnet/Armament.Server.sln' --verify-no-changes

    dotnet run --project 'shared-sim/Tests/SharedSim.Tests/Armament.SharedSim.Tests.csproj'
    dotnet run --project 'shared-sim/Tests/ContentValidation.Tests/Armament.ContentValidation.Tests.csproj'
    dotnet run --project 'server-dotnet/Tests/GameServer.Tests/Armament.GameServer.Tests.csproj'
    dotnet run --project 'server-dotnet/Tests/Persistence.Tests/Armament.Persistence.Tests.csproj'

    & "$RootDir/ops/scripts/run-unity-tests.ps1"
}
finally {
    Pop-Location
}
