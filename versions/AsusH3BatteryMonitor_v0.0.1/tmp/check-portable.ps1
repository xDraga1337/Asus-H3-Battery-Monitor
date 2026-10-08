$ErrorActionPreference = 'Stop'
$versionRoot = 'D:\programming\BIGPROJECTS\AsusH3BatteryMonitor\versions\AsusH3BatteryMonitor_v0.0.1'
$portableFolder = Join-Path $versionRoot 'tmp\portable-check'
New-Item -ItemType Directory -Force $portableFolder | Out-Null
Copy-Item (Join-Path $versionRoot 'publish\AsusH3BatteryMonitor.exe') $portableFolder

# Only the EXE is copied. Hide external runtime locations for this process and
# keep bundled native-library extraction inside this version's temporary folder.
$env:DOTNET_ROOT = Join-Path $versionRoot 'tmp\no-installed-runtime'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $versionRoot 'tmp\bundle-extract'
$env:TEMP = Join-Path $versionRoot 'tmp'
$env:TMP = $env:TEMP
Push-Location $portableFolder
try {
    $output = & '.\AsusH3BatteryMonitor.exe' --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Copied standalone executable self-test failed' }
    if (@(Get-ChildItem -File $portableFolder).Count -ne 1) { throw 'Unexpected companion files' }
    $output | Select-Object -Last 1
    'PASS: isolated single EXE runs with external runtime roots disabled; no companion files required.'
} finally {
    Pop-Location
}
