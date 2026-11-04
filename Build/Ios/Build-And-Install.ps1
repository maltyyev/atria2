<#
.SYNOPSIS
Builds the iOS app, verifies its bundle, then installs it through the existing USB relay.
#>

# Compile-Object.ps1 uses APIs absent from Windows PowerShell 5.1.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $pwshPath = if ($pwsh) { $pwsh.Source } else { $null }
    if (-not $pwshPath) {
        $candidates = @()
        if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe' }
        if ($env:USERPROFILE) { $candidates += Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' }
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $pwshPath = $candidate; break }
        }
    }
    if (-not $pwshPath) {
        Write-Error 'PowerShell 7 was not found on PATH, in Program Files, or in the Codex runtime.'
        exit 1
    }
    & $pwshPath -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand([string]$Stage, [string]$Executable, [string[]]$ArgumentList) {
    Write-Host "==> $Stage"
    & $Executable @ArgumentList
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "$Stage failed with exit code $code." }
}

function ConvertTo-WslPath([string]$Wsl, [string]$Distro, [string]$WindowsPath) {
    $output = & $Wsl -d $Distro --exec wslpath -a $WindowsPath
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "Could not translate path for WSL (exit $code): $WindowsPath" }
    $path = ($output -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($path)) { throw "WSL returned an empty path for: $WindowsPath" }
    return $path
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stageInput = if ($env:ATRIA_STAGE) { $env:ATRIA_STAGE } else { Join-Path $repo 'artifacts\stage\ios' }
if (-not [IO.Path]::IsPathFullyQualified($stageInput)) { throw "ATRIA_STAGE must be an absolute Windows path: $stageInput" }
$stage = [IO.Path]::GetFullPath($stageInput)
$distro = 'Atria-iOS'
$compileScript = Join-Path $PSScriptRoot 'Compile-Object.ps1'
$prepareScript = Join-Path $PSScriptRoot 'Prepare-Assets.py'
$buildScript = Join-Path $PSScriptRoot 'Build-Host.sh'
$verifyScript = Join-Path $PSScriptRoot 'Verify-App.py'
$app = Join-Path $stage 'Host\xtool\AtriaProbe.app'
$python = (Get-Command python -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pwshPath = Join-Path $PSHOME 'pwsh.exe'
if (-not (Test-Path -LiteralPath $pwshPath -PathType Leaf)) {
    $pwshPath = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}
$wsl = (Get-Command wsl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$previousStage = $env:ATRIA_STAGE

try {
    $env:ATRIA_STAGE = $stage
    Invoke-CheckedCommand 'Compile NativeAOT ARM64 object' $pwshPath @('-NoProfile', '-File', $compileScript)
    Invoke-CheckedCommand 'Stage and verify shared resources' $python @($prepareScript)

    $linuxStage = ConvertTo-WslPath $wsl $distro $stage
    $linuxBuildScript = ConvertTo-WslPath $wsl $distro $buildScript
    Invoke-CheckedCommand 'Build and link the Swift iOS app' $wsl @('-d', $distro, '--exec', 'env', "ATRIA_STAGE=$linuxStage", 'bash', $linuxBuildScript)

    if (-not (Test-Path -LiteralPath $app -PathType Container)) { throw "The Swift build did not produce the expected app: $app" }
    Invoke-CheckedCommand 'Verify ARM64 executable and bundled resources' $python @($verifyScript, $app)

    $linuxApp = $linuxStage.TrimEnd('/') + '/Host/xtool/AtriaProbe.app'
    $relay = $env:USBMUXD_SOCKET_ADDRESS
    if (-not $relay) {
        Write-Host 'Resolving the existing WSL USB relay gateway.'
        $routes = & $wsl -d $distro --exec ip route
        $code = $LASTEXITCODE
        if ($code -ne 0) { throw "Could not read the WSL default route (exit $code)." }
        $gateway = $null
        foreach ($route in $routes) {
            if ($route -match '^default via (\S+)') { $gateway = $Matches[1]; break }
        }
        if (-not $gateway) { throw 'No WSL default gateway was found; set USBMUXD_SOCKET_ADDRESS to the existing relay endpoint.' }
        $relay = if ($gateway.Contains(':')) { "[$gateway]:27015" } else { "$gateway`:27015" }
    }
    Invoke-CheckedCommand 'Install verified app over the existing USB relay' $wsl @('-d', $distro, '--exec', 'env', "USBMUXD_SOCKET_ADDRESS=$relay", 'xtool', 'install', '--usb', $linuxApp)
    Write-Host "Installed verified iOS app: $app"
}
finally {
    if ($null -eq $previousStage) { Remove-Item Env:ATRIA_STAGE -ErrorAction SilentlyContinue }
    else { $env:ATRIA_STAGE = $previousStage }
}
