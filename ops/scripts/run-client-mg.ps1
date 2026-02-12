$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:DOTNET_CLI_HOME = Join-Path $RootDir '.dotnet_home'

Push-Location $RootDir
try {
    dotnet run --project 'client-mg/Armament.Client.MonoGame/Armament.Client.MonoGame.csproj'
}
finally {
    Pop-Location
}
