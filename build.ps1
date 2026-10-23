param([ValidateSet('probe','build','test','test-aot','publish')][string]$Action='build')
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
        & "$projectRoot/tools/video/build_codec.cmd"
        if($LASTEXITCODE -ne 0) { throw 'Native video adapter build failed.' }
    }
    switch ($Action) {
        'probe' {
            & dotnet publish "$workspaceRoot/audit/csharp-probe/Probe.csproj" -c Release --configfile "$projectRoot/NuGet.Config" -o "$workspaceRoot/audit/csharp-probe/publish"
            if ($LASTEXITCODE -eq 0) { & "$workspaceRoot/audit/csharp-probe/publish/Probe.exe" }
        }
        'build' { & dotnet build Atria.slnx -c Release -m:1 -nr:false }
        'test' { & dotnet run --project tests/Atria.Tests.csproj -c Release -- "$workspaceRoot/Atria 2" "$workspaceRoot/music" }
        'test-aot' {
            & dotnet publish tests/Atria.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -m:1 -nr:false -o artifacts/tests-aot
            if($LASTEXITCODE -eq 0) { & "$projectRoot/artifacts/tests-aot/Atria.Tests.exe" "$workspaceRoot/Atria 2" "$workspaceRoot/music" }
        }
        'publish' { & dotnet publish src/Atria.Game.csproj -c Release -m:1 -nr:false -o artifacts/win-x64 }
    }
    if ($LASTEXITCODE -ne 0) { throw "Action $Action failed with exit code $LASTEXITCODE" }
    if($Action -eq 'publish') {
        $publishedMusic=Join-Path $projectRoot 'artifacts/win-x64/assets/music'
        New-Item -ItemType Directory -Path $publishedMusic -Force | Out-Null
        foreach($trackNumber in 2..28) {
            $trackName='Track{0:00}.wav' -f $trackNumber
            Copy-Item -LiteralPath (Join-Path "$workspaceRoot/music" $trackName) -Destination (Join-Path $publishedMusic $trackName) -Force
        }
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
