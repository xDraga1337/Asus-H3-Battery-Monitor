# Build and verify a local development EXE; does not create a release.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'This script requires Windows to build and test the Windows x64 executable.'
}

$repo = $PSScriptRoot
$project = Join-Path $repo 'src\AsusH3BatteryMonitor.csproj'
$dev = Join-Path $repo 'dev'
$exe = Join-Path $dev 'AsusH3BatteryMonitor.exe'

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File failed with exit code $LASTEXITCODE."
    }
}

# Prefer the local SDK host, then PATH and the standard Windows installation.
$candidates = @('C:\Users\M\.dotnet\dotnet.exe')
$onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($onPath) { $candidates += $onPath.Source }
if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }

New-Item -ItemType Directory -Path $dev -Force | Out-Null
# A failed invocation must never leave an earlier EXE looking like its result.
if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }

try {
    $dotnet = $null
    $sdk = $null
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $installed = @(& $candidate --list-sdks)
        if ($LASTEXITCODE -ne 0) { continue }
        $versions = @($installed | ForEach-Object {
            if ($_ -match '^(10\.0\.\d+)\s+\[') { [version]$Matches[1] }
        } | Sort-Object -Descending)
        if ($versions.Count -gt 0) {
            $dotnet = $candidate
            $sdk = $versions[0].ToString()
            break
        }
    }
    if (-not $dotnet) {
        throw 'No compatible stable .NET 10 SDK found. Install a .NET 10 SDK and add dotnet.exe to PATH.'
    }

    # SDK selection uses the working directory. An isolated global.json allows a
    # compatible fallback without editing the repository's pinned global.json.
    $work = Join-Path $repo ('tmp\build-dev-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $sdkConfig = @{ sdk = @{ version = $sdk; rollForward = 'disable'; allowPrerelease = $false } }
    $sdkConfig | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $work 'global.json') -Encoding ASCII
    $staging = Join-Path $work 'publish'
    Push-Location $work
    try {
        Write-Host "Building with $dotnet (SDK $sdk)"
        Invoke-Checked -File $dotnet -Arguments @('run', '--project', $project, '--configuration', 'Release', '-p:RestoreLockedMode=true', '--', '--self-test')
        Invoke-Checked -File $dotnet -Arguments @('publish', $project, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:RestoreLockedMode=true', '--no-restore', '--output', $staging)
        $freshExe = Join-Path $staging 'AsusH3BatteryMonitor.exe'
        if (-not (Test-Path -LiteralPath $freshExe -PathType Leaf)) {
            throw 'Publish completed without producing the expected development executable.'
        }
        Copy-Item -LiteralPath $freshExe -Destination $exe
        Invoke-Checked -File $exe -Arguments @('--self-test')
    }
    finally { Pop-Location }
    Write-Host "Development build verified: $exe"
}
catch {
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    throw
}
