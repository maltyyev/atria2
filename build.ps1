param([ValidateSet('probe','build','test','test-aot','publish')][string]$Action='build')

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Build/Windows/build.ps1'
& $runner -Action $Action
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
