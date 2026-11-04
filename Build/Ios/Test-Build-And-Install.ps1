<#
.SYNOPSIS
Checks the launcher order, PowerShell 5.1 handoff, spaced paths, and install failure gate with stub processes.
#>
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "atria ios launcher test $PID"
$repo = Join-Path $testRoot 'repository with spaces'
$ios = Join-Path $repo 'Build\Ios'
$bin = Join-Path $testRoot 'stub commands'
$calls = Join-Path $testRoot 'calls.log'
$stage = Join-Path $repo 'artifacts\stage with spaces'
$app = Join-Path $stage 'Host\xtool\AtriaProbe.app'
$runner = Join-Path $ios 'Build-And-Install.ps1'
$powershell = (Get-Command powershell.exe -CommandType Application -ErrorAction Stop).Source
$saved = @{}
foreach ($name in @('PATH', 'ATRIA_STAGE', 'USBMUXD_SOCKET_ADDRESS', 'ATRIA_TEST_CALL_LOG', 'ATRIA_TEST_APP', 'ATRIA_TEST_FAIL_STAGE')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    New-Item -ItemType Directory -Force -Path $ios, $bin | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build-And-Install.ps1') -Destination $runner
    @('Prepare-Assets.py', 'Build-Host.sh', 'Verify-App.py') | ForEach-Object { Set-Content -LiteralPath (Join-Path $ios $_) -Value '' }
    Set-Content -LiteralPath (Join-Path $ios 'Compile-Object.ps1') -Value @'
Add-Content -LiteralPath $env:ATRIA_TEST_CALL_LOG -Value 'COMPILE'
if ($env:ATRIA_TEST_FAIL_STAGE -eq 'compile') { exit 31 }
'@
    Set-Content -LiteralPath (Join-Path $bin 'python.cmd') -Value @'
@echo off
echo TEST_FAIL=%ATRIA_TEST_FAIL_STAGE%>>"%ATRIA_TEST_CALL_LOG%"
echo(%*| findstr /I /C:"Prepare-Assets.py" >nul
if errorlevel 1 goto verify
echo PREPARE>>"%ATRIA_TEST_CALL_LOG%"
if /I "%ATRIA_TEST_FAIL_STAGE%"=="prepare" exit /b 32
exit /b 0
:verify
echo(%*| findstr /I /C:"Verify-App.py" >nul
if errorlevel 1 exit /b 90
echo VERIFY>>"%ATRIA_TEST_CALL_LOG%"
if /I "%ATRIA_TEST_FAIL_STAGE%"=="verify" exit /b 33
exit /b 0
'@
    Set-Content -LiteralPath (Join-Path $bin 'wsl.cmd') -Value @'
@echo off
echo WSL-ARGS %*>>"%ATRIA_TEST_CALL_LOG%"
echo(%*| findstr /I /C:"wslpath" >nul
if not errorlevel 1 (
  echo WSLPATH>>"%ATRIA_TEST_CALL_LOG%"
  echo(%*| findstr /I /C:"Build-Host.sh" >nul
  if not errorlevel 1 (echo /mnt/c/repository with spaces/Build-Host.sh&exit /b 0)
  echo /mnt/c/repository with spaces/stage with spaces
  exit /b 0
)
echo(%*| findstr /I /C:"Build-Host.sh" >nul
if errorlevel 1 goto install
echo HOST_BUILD>>"%ATRIA_TEST_CALL_LOG%"
if /I "%ATRIA_TEST_FAIL_STAGE%"=="host" exit /b 42
if defined ATRIA_TEST_APP mkdir "%ATRIA_TEST_APP%" >nul 2>nul
exit /b 0
:install
echo(%*| findstr /I /C:"xtool install" >nul
if not errorlevel 1 (echo INSTALL>>"%ATRIA_TEST_CALL_LOG%"&exit /b 0)
exit /b 91
'@

    $env:PATH = "$bin;$($saved['PATH'])"
    $env:ATRIA_STAGE = $stage
    $env:USBMUXD_SOCKET_ADDRESS = '192.168.48.1:27015'
    $env:ATRIA_TEST_CALL_LOG = $calls
    $env:ATRIA_TEST_APP = $app
    Remove-Item Env:ATRIA_TEST_FAIL_STAGE -ErrorAction SilentlyContinue
    $wslStub = (Get-Command wsl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    if ([IO.Path]::GetFullPath($wslStub) -ne [IO.Path]::GetFullPath((Join-Path $bin 'wsl.cmd'))) {
        throw "The stub WSL command did not take precedence; refusing to run a real WSL build: $wslStub"
    }

    & $powershell -NoProfile -ExecutionPolicy Bypass -File $runner 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Mock success run failed with exit code $LASTEXITCODE." }
    $success = Get-Content -LiteralPath $calls
    $ordered = @('COMPILE', 'PREPARE', 'HOST_BUILD', 'VERIFY', 'INSTALL')
    $last = -1
    foreach ($event in $ordered) {
        $index = [Array]::IndexOf($success, $event)
        if ($index -le $last) { throw "Missing or out-of-order success event: $event" }
        $last = $index
    }
    if (-not ($success -match [regex]::Escape($stage))) { throw 'ATRIA_STAGE with spaces did not survive the PowerShell 5.1 handoff.' }
    if (-not ($success -match [regex]::Escape('/mnt/c/repository with spaces/stage with spaces/Host/xtool/AtriaProbe.app'))) {
        throw 'The spaced Linux app path did not reach the install process.'
    }

    foreach ($failureStage in @('compile', 'prepare', 'host', 'verify')) {
        Remove-Item -LiteralPath $calls -Force
        if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Recurse -Force }
        $env:ATRIA_TEST_FAIL_STAGE = $failureStage
        & $powershell -NoProfile -ExecutionPolicy Bypass -File $runner 2>&1 | Out-Null
        $failureCode = $LASTEXITCODE
        $failure = Get-Content -LiteralPath $calls
        if ($failureCode -eq 0 -or $failure -contains 'INSTALL') { throw "A failed $failureStage stage did not block installation (exit=$failureCode; events=$($failure -join ','))." }
        $expectedEvent = @{ compile='COMPILE'; prepare='PREPARE'; host='HOST_BUILD'; verify='VERIFY' }[$failureStage]
        if ($failure -notcontains $expectedEvent) { throw "The mocked $failureStage failure did not reach its stage." }
    }
    Write-Host 'Build-And-Install mocked flow passed: order, spaced paths, PS 5.1 handoff, and all install gates.'
}
finally {
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    }
    $tempRoot = [IO.Path]::GetFullPath($testRoot)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($tempRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
