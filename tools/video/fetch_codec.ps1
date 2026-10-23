$ErrorActionPreference='Stop'
$atriaProjectRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$atriaArchiveName='ffmpeg-n9.0.2-33-g4cf298e66a-win64-lgpl-shared-9.0.zip'
$atriaExpected='14d2da681ed1999a6232de61f99d869a46df437dd910d69c1fb5c9c83172242e'
$atriaUrl="https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-22-13-06/$atriaArchiveName"
$atriaDownloads=Join-Path $atriaProjectRoot 'third_party/downloads'
New-Item -ItemType Directory -Force -Path $atriaDownloads | Out-Null
$atriaArchive=Join-Path $atriaDownloads $atriaArchiveName
if(!(Test-Path -LiteralPath $atriaArchive)) { Invoke-WebRequest -Uri $atriaUrl -OutFile $atriaArchive }
if((Get-FileHash -LiteralPath $atriaArchive -Algorithm SHA256).Hash -ne $atriaExpected) { throw 'Pinned video dependency SHA256 mismatch.' }
$atriaDestination=Join-Path $atriaProjectRoot 'third_party/ffmpeg'
$atriaExpanded=Join-Path $atriaDestination ([IO.Path]::GetFileNameWithoutExtension($atriaArchiveName))
if(!(Test-Path -LiteralPath $atriaExpanded)) { Expand-Archive -LiteralPath $atriaArchive -DestinationPath $atriaDestination }
Write-Output "Pinned video codec available: $atriaExpanded"
