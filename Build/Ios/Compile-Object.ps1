$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$packages = if ($env:ATRIA_PACKAGES) { $env:ATRIA_PACKAGES } else { Join-Path $projectRoot '.packages' }
$stageInput = if ($env:ATRIA_STAGE) { $env:ATRIA_STAGE } else { Join-Path $projectRoot 'artifacts\stage\ios' }
if (![IO.Path]::IsPathFullyQualified($stageInput)) { throw "ATRIA_STAGE must be an absolute Windows path: $stageInput" }
$stageRoot = [IO.Path]::GetFullPath($stageInput)
$repoRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowedStageRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\stage'))
function Test-PathWithin([string]$Path, [string]$Parent) {
    if ($Path.Equals($Parent, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}
if ($stageRoot.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-PathWithin $repoRoot $stageRoot)) {
    throw "ATRIA_STAGE may not be the repository or one of its parents: $stageRoot"
}
if ((Test-PathWithin $stageRoot $repoRoot) -and
    ($stageRoot.Equals($allowedStageRoot, [StringComparison]::OrdinalIgnoreCase) -or !(Test-PathWithin $stageRoot $allowedStageRoot))) {
    throw "ATRIA_STAGE inside the repository must be a child of artifacts/stage: $stageRoot"
}
$current = [IO.Path]::GetPathRoot($stageRoot)
$relative = $stageRoot.Substring($current.Length)
$separators = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
foreach ($segment in $relative.Split($separators, [StringSplitOptions]::RemoveEmptyEntries)) {
    $current = Join-Path $current $segment
    $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "ATRIA_STAGE may not traverse a link: $current"
    }
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
$env:ATRIA_STAGE = $stageRoot
$objectPath = Join-Path $stageRoot 'Game.ios-arm64.o'
$responsePath = Join-Path $stageRoot 'ilc-ios.rsp'
$compilerLogPath = Join-Path $stageRoot 'ilc-ios.log'
foreach ($outputPath in @($objectPath, $responsePath, $compilerLogPath)) {
    $outputItem = Get-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
    if ($outputItem -and ($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to write through a linked staging output: $outputPath"
    }
}
$version = '10.0.13'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = $packages
Push-Location $PSScriptRoot
try {
    $hostDirectory = Join-Path $projectRoot 'Src\Hosts\Ios'
    $platformAssembly = Join-Path $projectRoot 'Src\Platforms\Ios\bin\Release\net10.0\Atria.Platforms.Ios.dll'
    & dotnet build (Join-Path $hostDirectory 'Atria.Ios.csproj') -c Release -p:PublishAot=false -p:NuGetAudit=false -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Managed compilation failed.' }
    $compiler = Join-Path $packages "runtime.win-x64.microsoft.dotnet.ilcompiler\$version\tools\ilc.exe"
    $references = Join-Path $packages "microsoft.netcore.app.runtime.nativeaot.ios-arm64\$version\runtimes\ios-arm64\lib\net10.0"
    $assembly = Join-Path $hostDirectory 'bin\Release\net10.0\Atria.Ios.dll'
    $coreAssembly = Join-Path $projectRoot 'Src\Core\bin\Release\net10.0\Atria.Core.dll'
    $arguments = @(
        $assembly
        ('-r:' + $coreAssembly)
        ('-r:' + $platformAssembly)
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
    & $compiler "@$responsePath" *> $compilerLogPath
    $compilerExit = $LASTEXITCODE
    Get-Content $compilerLogPath
    if ($compilerExit -ne 0) { throw "ILC exited with $compilerExit" }
    $bytes = [IO.File]::ReadAllBytes($objectPath)
    if ($bytes.Length -lt 32 -or [BitConverter]::ToString($bytes, 0, 4) -ne 'CF-FA-ED-FE' -or
        [BitConverter]::ToUInt32($bytes, 4) -ne 0x0100000c -or [BitConverter]::ToUInt32($bytes, 12) -ne 1) {
        throw 'Expected a Mach-O 64-bit ARM64 relocatable object.'
    }
    Write-Output "PASS: ARM64 Mach-O object, $($bytes.Length) bytes. Linking and execution are NOT verified."
} finally { Pop-Location }
