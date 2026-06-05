# Apply local git identity settings to an existing repository.
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

if (-not (Test-Path .git)) {
	throw "No .git directory found."
}

git config --local user.name "i10u"
git config --local user.email "9254361+i10u@users.noreply.github.com"
git config --local core.hooksPath ".githooks"

Write-Host "Local git identity:"
git config --local --get user.name
git config --local --get user.email
Write-Host "Hooks path:"
git config --local --get core.hooksPath

Pop-Location
