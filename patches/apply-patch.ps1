<#
.SYNOPSIS
    Unlock Minecraft download / install / repair / modpack-install in PCL Community Edition.

.DESCRIPTION
    Applies patches\0001-unlock-downloads-mainland-china.patch, which reverts upstream
    commit 8cd9d374 "fix(install): mainland China disables Minecraft installation (#3673)".

    The patch removes the region gate that blocks Minecraft download, file repair,
    version install and modpack install based on Windows region / locale / time zone.
    It is idempotent: running it twice is a no-op.

.PARAMETER RepoPath
    Source checkout to patch. Defaults to the parent folder of this script.

.PARAMETER PatchFile
    Patch to apply. Defaults to 0001-unlock-downloads-mainland-china.patch next to this script.

.PARAMETER Build
    Publish a single-file exe after patching.

.PARAMETER Configuration
    Build configuration: Release (default), Beta or Debug.

.PARAMETER Platform
    x64 (default) or ARM64.

.PARAMETER OutputDir
    Publish output folder. Defaults to <RepoPath>\artifacts\pcl-ce-unlocked.

.PARAMETER Dotnet
    dotnet executable to use.

.EXAMPLE
    .\apply-patch.ps1 -Build
    .\apply-patch.ps1 -RepoPath D:\src\PCL-CE -Build -Configuration Beta -Platform ARM64
#>
[CmdletBinding()]
param(
    [string] $RepoPath,
    [string] $PatchFile,
    [switch] $Build,
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64',
    [string] $OutputDir,
    [string] $Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $RepoPath)   { $RepoPath   = Split-Path -Parent $scriptDir }
if (-not $PatchFile)  { $PatchFile  = Join-Path $scriptDir '0001-unlock-downloads-mainland-china.patch' }
if (-not $OutputDir)  { $OutputDir  = Join-Path $RepoPath 'artifacts\pcl-ce-unlocked' }

$RepoPath  = (Resolve-Path $RepoPath).Path
$PatchFile = (Resolve-Path $PatchFile).Path
$gateFile  = Join-Path $RepoPath 'Plain Craft Launcher 2\Modules\Minecraft\RestrictedFeaturePolicy.cs'

Write-Host "Repository : $RepoPath"
Write-Host "Patch      : $PatchFile"

# ---- preflight -------------------------------------------------------------
if (-not (Test-Path (Join-Path $RepoPath '.git'))) {
    throw "'$RepoPath' is not a git checkout. Clone https://github.com/StillMi/PCL-CEPatch first."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git was not found in PATH.'
}

function Get-GateResidue {
    # Returns matching lines if any region-gate symbol is still referenced.
    $output = & git -C $RepoPath grep -n -E 'RestrictedFeaturePolicy|RegionPolicy|RegionAssessment|RegionClassification' -- '*.cs' 2>$null
    if ($LASTEXITCODE -eq 0) { return @($output) }
    return @()
}

$residue = Get-GateResidue
if ($residue.Count -eq 0 -and -not (Test-Path $gateFile)) {
    Write-Host '[skip] Patch already applied - no region gate found in this checkout.' -ForegroundColor Yellow
} else {
    Write-Host '[run ] Applying patch ...' -ForegroundColor Cyan
    & git -C $RepoPath apply --3way --whitespace=nowarn $PatchFile
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'git apply --3way failed. Retrying with --reject (conflict files will be created).'
        & git -C $RepoPath apply --reject --whitespace=nowarn $PatchFile
        if ($LASTEXITCODE -ne 0) {
            throw @'
Patch could not be applied automatically.

The patch targets upstream commit 915ac297 (branch dev, 2026-10-23).
If your checkout has newer commits, resolve the *.rej files manually or rebase.
'@
        }
    }

    $residue = Get-GateResidue
    if (Test-Path $gateFile) {
        throw "Patch applied but '$gateFile' still exists."
    }
    if ($residue.Count -gt 0) {
        throw ("Patch applied but region-gate references remain:`n" + ($residue -join "`n"))
    }
    Write-Host '[ok  ] Patch applied. Minecraft download/install unlocked.' -ForegroundColor Green
}

# ---- optional build --------------------------------------------------------
if ($Build) {
    $csproj = Join-Path $RepoPath 'Plain Craft Launcher 2\Plain Craft Launcher 2.csproj'
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    Write-Host "[run ] Publishing $Configuration|$Platform ..." -ForegroundColor Cyan
    & $Dotnet publish $csproj -c $Configuration -p:Platform=$Platform `
        -p:DeleteExistingFiles=true -o $OutputDir --no-self-contained
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $exe = Join-Path $OutputDir 'Plain Craft Launcher 2.exe'
    if (-not (Test-Path $exe)) { throw "Publish finished but '$exe' is missing." }
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
    Write-Host "[ok  ] Output: $exe ($size MB)" -ForegroundColor Green
    Write-Host '       Requires the .NET 10 Desktop Runtime on the target machine.'
}

Write-Host 'Done.'
