<#
.SYNOPSIS
    Bumps the patch version, commits, tags, pushes, and starts the self-hosted
    GitHub Actions runner to build & publish the release.

.DESCRIPTION
    1. Reads <Version> from MissileCamNO.csproj, increments the patch number by 1,
       and writes the new version back into the .csproj.
    2. Commits the change with the message "Update version to <versionNumber>".
    3. Tags the commit "v<versionNumber>" (the release workflow triggers on v* tags).
    4. Pushes the commit and the newly created tag to origin.
    5. Starts the self-hosted GitHub Actions runner (C:\actions-runner\run.cmd),
       which picks up the pushed tag and builds/publishes the release.

.EXAMPLE
    ./releaseUpdate.ps1
#>

[CmdletBinding()]
param(
    [string]$CsprojPath   = (Join-Path $PSScriptRoot 'MissileCamNO.csproj'),
    [string]$RunnerPath   = 'C:\actions-runner'
)

$ErrorActionPreference = 'Stop'

# --- 1. Bump the patch version in the .csproj ------------------------------
if (-not (Test-Path -LiteralPath $CsprojPath)) {
    throw "Cannot find project file: $CsprojPath"
}

$content = Get-Content -LiteralPath $CsprojPath -Raw

$match = [regex]::Match($content, '<Version>(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)</Version>')
if (-not $match.Success) {
    throw "Could not find a <Version>major.minor.patch</Version> element in $CsprojPath"
}

$major = [int]$match.Groups['major'].Value
$minor = [int]$match.Groups['minor'].Value
$patch = [int]$match.Groups['patch'].Value

$oldVersion   = "$major.$minor.$patch"
$patch       += 1
$versionNumber = "$major.$minor.$patch"
$tagName       = "v$versionNumber"

Write-Host "Bumping version: $oldVersion -> $versionNumber" -ForegroundColor Cyan

$newContent = $content -replace [regex]::Escape($match.Value), "<Version>$versionNumber</Version>"
Set-Content -LiteralPath $CsprojPath -Value $newContent -NoNewline -Encoding UTF8

# --- 2. Commit ------------------------------------------------------------
$commitMessage = "Update version to $versionNumber"

git add -- $CsprojPath
if ($LASTEXITCODE -ne 0) { throw "git add failed (exit $LASTEXITCODE)." }

git commit -m $commitMessage
if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)." }

# --- 3. Tag ---------------------------------------------------------------
git tag $tagName
if ($LASTEXITCODE -ne 0) { throw "git tag '$tagName' failed (exit $LASTEXITCODE)." }

# --- 4. Push commit + the new tag -----------------------------------------
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "git push (commit) failed (exit $LASTEXITCODE)." }

git push origin $tagName
if ($LASTEXITCODE -ne 0) { throw "git push (tag $tagName) failed (exit $LASTEXITCODE)." }

Write-Host "Pushed commit and tag $tagName to origin." -ForegroundColor Green

# --- 5. Start the self-hosted GitHub Actions runner -----------------------
$runCmd = Join-Path $RunnerPath 'run.cmd'
if (-not (Test-Path -LiteralPath $runCmd)) {
    throw "Cannot find the Actions runner at: $runCmd"
}

Write-Host "Starting GitHub Actions runner: $runCmd" -ForegroundColor Cyan
Push-Location $RunnerPath
try {
    & $runCmd
}
finally {
    Pop-Location
}
