$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$packages = Join-Path $projectRoot '.packages'
$version = '10.0.13'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = $packages
Push-Location $PSScriptRoot
try {
    & dotnet build (Join-Path $PSScriptRoot 'Managed\Atria.Ios.csproj') -c Release -p:PublishAot=false -p:NuGetAudit=false -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Managed compilation failed.' }
    $compiler = Join-Path $packages "runtime.win-x64.microsoft.dotnet.ilcompiler\$version\tools\ilc.exe"
    $references = Join-Path $packages "microsoft.netcore.app.runtime.nativeaot.ios-arm64\$version\runtimes\ios-arm64\lib\net10.0"
    $assembly = Join-Path $PSScriptRoot 'Managed\bin\Release\net10.0\Atria.Ios.dll'
    $objectPath = Join-Path $PSScriptRoot 'Game.ios-arm64.o'
    $responsePath = Join-Path $PSScriptRoot 'ilc-ios.rsp'
    $arguments = @(
        $assembly
        "-o:$objectPath"
        '--targetos:ios'
        '--targetarch:arm64'
        '--nativelib'
        '--export-unmanaged-entrypoints'
        '--scanreflection'
        '--stacktracedata'
        '--generateunmanagedentrypoints:System.Private.CoreLib,HIDDEN'
        '--initassembly:System.Private.CoreLib'
        '--initassembly:System.Private.StackTraceMetadata'
        '--initassembly:System.Private.TypeLoader'
        '--initassembly:System.Private.Reflection.Execution'
        '--directpinvoke:__Internal'
        '--feature:System.StartupHookProvider.IsSupported=false'
        '--directpinvoke:System.Native'
        '--directpinvoke:System.IO.Compression.Native'
        '--directpinvoke:System.Net.Security.Native'
        '--directpinvoke:System.Security.Cryptography.Native.Apple'
        '--feature:System.Globalization.Invariant=true'
        '--feature:System.Diagnostics.Tracing.EventSource.IsSupported=false'
        '--feature:System.Reflection.Metadata.MetadataUpdater.IsSupported=false'
        '--feature:System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported=false'
        '--Os'
    )
    $arguments += Get-ChildItem -LiteralPath $references -Filter '*.dll' | ForEach-Object { '-r:' + $_.FullName }
    $nativeReferences = Join-Path $packages "microsoft.netcore.app.runtime.nativeaot.ios-arm64\$version\runtimes\ios-arm64\native"
    $arguments += Get-ChildItem -LiteralPath $nativeReferences -Filter '*.dll' | ForEach-Object { '-r:' + $_.FullName }
    [IO.File]::WriteAllLines($responsePath, $arguments, [Text.UTF8Encoding]::new($false))
    & $compiler "@$responsePath" *> (Join-Path $PSScriptRoot 'ilc-ios.log')
    $compilerExit = $LASTEXITCODE
    Get-Content (Join-Path $PSScriptRoot 'ilc-ios.log')
    if ($compilerExit -ne 0) { throw "ILC exited with $compilerExit" }
    $bytes = [IO.File]::ReadAllBytes($objectPath)
    if ($bytes.Length -lt 32 -or [BitConverter]::ToString($bytes, 0, 4) -ne 'CF-FA-ED-FE' -or
        [BitConverter]::ToUInt32($bytes, 4) -ne 0x0100000c -or [BitConverter]::ToUInt32($bytes, 12) -ne 1) {
        throw 'Expected a Mach-O 64-bit ARM64 relocatable object.'
    }
    Write-Output "PASS: ARM64 Mach-O object, $($bytes.Length) bytes. Linking and execution are NOT verified."
} finally { Pop-Location }
