param([ValidateSet('probe','build','test','test-aot','publish')][string]$Action='build')
# Clone with Git LFS installed. For an existing checkout, run git lfs pull before building.
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$workspaceRoot=Split-Path -Parent $projectRoot
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.dotnet-home'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'
Push-Location $projectRoot
try {
    if($Action -ne 'probe') {
        [xml]$assets = Get-Content -LiteralPath "$projectRoot/Assets/PackageResources.props" -Raw
        foreach ($entry in @($assets.Project.ItemGroup.GameResource.Include, $assets.Project.ItemGroup.MusicResource.Include)) {
            foreach ($resource in $entry.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
                $assetPath = $resource.Trim().Replace('$(MSBuildThisFileDirectory)', "$projectRoot/Assets/")
                if (!(Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                    throw "Missing asset: $assetPath. Install Git LFS and run git lfs pull."
                }
                $stream = [IO.File]::OpenRead($assetPath)
                try {
                    $header = [byte[]]::new(64)
                    $length = $stream.Read($header, 0, $header.Length)
                    if ([Text.Encoding]::ASCII.GetString($header, 0, $length).StartsWith('version https://git-lfs.github.com/spec/v1')) {
                        throw "Asset contains a Git LFS pointer: $assetPath. Run git lfs pull to download the actual game resources."
                    }
                } finally { $stream.Dispose() }
            }
        }
        $codecHeader=Join-Path $projectRoot 'third_party/ffmpeg/ffmpeg-n9.0.2-33-g4cf298e66a-win64-lgpl-shared-9.0/include/libavcodec/avcodec.h'
        if(!(Test-Path -LiteralPath $codecHeader)) { & "$projectRoot/tools/video/fetch_codec.ps1" }
        & "$projectRoot/tools/video/build_codec.cmd"
        if($LASTEXITCODE -ne 0) { throw 'Native video adapter build failed.' }
    }
    switch ($Action) {
        'probe' {
            & dotnet publish "$workspaceRoot/audit/csharp-probe/Probe.csproj" -c Release --configfile "$projectRoot/NuGet.Config" -o "$workspaceRoot/audit/csharp-probe/publish"
            if ($LASTEXITCODE -eq 0) { & "$workspaceRoot/audit/csharp-probe/publish/Probe.exe" }
        }
        'build' { & dotnet build Atria.slnx -c Release -m:1 -nr:false }
        'test' { & dotnet run --project Tests/Atria.Tests.csproj -c Release -- "$projectRoot/Assets/Game" "$projectRoot/Assets/Music" }
        'test-aot' {
            & dotnet publish Tests/Atria.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -m:1 -nr:false -o artifacts/tests-aot
            if($LASTEXITCODE -eq 0) { & "$projectRoot/artifacts/tests-aot/Atria.Tests.exe" "$projectRoot/Assets/Game" "$projectRoot/Assets/Music" }
        }
        'publish' { & dotnet publish Src/Atria.Game.csproj -c Release -m:1 -nr:false -o artifacts/win-x64 }
    }
    if ($LASTEXITCODE -ne 0) { throw "Action $Action failed with exit code $LASTEXITCODE" }
    if($Action -eq 'publish') {
        & "$projectRoot/tools/verify_package.ps1"
        $publishedExe=Join-Path $projectRoot 'artifacts/win-x64/Atria.exe'
        $publishedVersion=(Get-Item -LiteralPath $publishedExe).VersionInfo.ProductVersion
        [ordered]@{
            version=$publishedVersion
            configuration='Release NativeAOT win-x64'
            publishedUtc=[DateTime]::UtcNow.ToString('O')
            executable='Atria.exe'
            sha256=(Get-FileHash -LiteralPath $publishedExe -Algorithm SHA256).Hash
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/win-x64/build.json') -Encoding utf8
    }
} finally { Pop-Location }
