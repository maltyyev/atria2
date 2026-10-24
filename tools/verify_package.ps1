param([string]$PackageRoot = (Join-Path $PSScriptRoot '../artifacts/win-x64'))

$ErrorActionPreference = 'Stop'
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Assets'))
$PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path
[xml]$definition = Get-Content -LiteralPath (Join-Path $assetRoot 'PackageResources.props') -Raw
$verified = 0

foreach ($group in @('Game', 'Music')) {
    $item = $definition.Project.ItemGroup.SelectSingleNode("${group}Resource")
    $expectedNames = @()
    $destination = Join-Path $PackageRoot ('assets/' + $group.ToLowerInvariant())

    foreach ($entry in $item.Include.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
        $relativePath = $entry.Trim().Replace('$(MSBuildThisFileDirectory)', '')
        $source = Join-Path $assetRoot $relativePath
        $name = Split-Path -Leaf $source
        $expectedNames += $name
        $deployed = Join-Path $destination $name

        if (!(Test-Path -LiteralPath $deployed -PathType Leaf)) {
            throw "Missing packaged resource: $deployed"
        }

        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $deployed -Algorithm SHA256).Hash) {
            throw "Packaged resource differs from the project asset: $deployed"
        }

        $verified++
    }

    $actualNames = @(Get-ChildItem -LiteralPath $destination -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($destination, $_.FullName)
    })
    if (Compare-Object $expectedNames $actualNames) {
        throw "Unexpected files in packaged $group assets. Package only the declared resources."
    }
}

foreach ($name in @('Atria.exe', 'atria_video.dll', 'avcodec-63.dll', 'avutil-61.dll', 'swscale-10.dll', 'swresample-7.dll', 'licenses/FFmpeg-LGPL-3.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $PackageRoot $name) -PathType Leaf)) {
        throw "Missing package dependency: $name"
    }
}

foreach ($source in Get-ChildItem -LiteralPath (Join-Path $assetRoot 'Localization') -Recurse -File -Filter '*.json') {
    $relative = [IO.Path]::GetRelativePath((Join-Path $assetRoot 'Localization'), $source.FullName)
    $deployed = Join-Path $PackageRoot ('assets/localization/' + $relative)
    if (!(Test-Path -LiteralPath $deployed -PathType Leaf)) {
        throw "Missing editable localization: $deployed"
    }
}

# Player-edited translations need validation, not equality with the source files.
& (Join-Path $PackageRoot 'Atria.exe') --validate-localization | Write-Output
if ($LASTEXITCODE -ne 0) {
    throw "Localization validation failed. See playtest-data/logs/localization-validation.log; existing translations were preserved."
}

Write-Output "Package verified: $verified unchanged resources, editable localization, native executable, video dependencies and license."
