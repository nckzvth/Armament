$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ProjectPath = Join-Path $RootDir 'client-unity'
$ResultsDir = Join-Path $RootDir '.artifacts/unity-tests'
New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null

$UnityBin = $env:UNITY_PATH
if (-not $UnityBin) {
    $candidates = Get-ChildItem '/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity' -ErrorAction SilentlyContinue | Sort-Object FullName
    if ($candidates) {
        $UnityBin = $candidates[-1].FullName
    }
}

if (-not $UnityBin -or -not (Test-Path $UnityBin)) {
    throw 'Unity executable not found. Set UNITY_PATH to Unity 6 editor binary.'
}

function Invoke-UnityTests([string]$platform) {
    $results = Join-Path $ResultsDir "$platform-results.xml"
    $log = Join-Path $ResultsDir "$platform.log"

    & $UnityBin `
      -batchmode `
      -nographics `
      -projectPath $ProjectPath `
      -runTests `
      -testPlatform $platform `
      -testResults $results `
      -logFile $log `
      -quit

    if ($LASTEXITCODE -ne 0) {
      throw "Unity $platform tests failed."
    }
}

Invoke-UnityTests -platform 'EditMode'
Invoke-UnityTests -platform 'PlayMode'
Write-Host "Unity batch tests completed. Results at $ResultsDir"
