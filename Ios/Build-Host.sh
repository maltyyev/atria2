#!/usr/bin/env bash
set -euo pipefail
ios_root="$(cd -- "$(dirname -- "$0")" && pwd)"
export PATH="/opt/atria-ios/swift/usr/bin:$PATH"
export ATRIA_NATIVEAOT_ROOT="${ATRIA_NATIVEAOT_ROOT:-$ios_root/../.packages/microsoft.netcore.app.runtime.nativeaot.ios-arm64/10.0.13/runtimes/ios-arm64/native}"
stage="${ATRIA_STAGE:-/opt/atria-ios/game-host}"
mkdir -p "$stage/Host"
# Rebuild the Swift source stage so removed and relocated types cannot survive a previous build.
rm -rf -- "$stage/Host/Sources"
cp -a "$ios_root/../Src/Hosts/Ios/Swift/Sources" "$stage/Host/"
mkdir -p "$stage/Host/Sources/AtriaProbe"
cp -a "$ios_root/../Src/Platforms/Ios/Swift/Sources/AtriaProbe/." "$stage/Host/Sources/AtriaProbe/"
cp "$ios_root/../Src/Hosts/Ios/Swift/Package.swift" "$ios_root/../Src/Hosts/Ios/Swift/xtool.yml" "$ios_root/../Src/Hosts/Ios/Swift/Game-Info.plist" "$stage/Host/"
cp "$ios_root/Game.ios-arm64.o" "$stage/"
cd "$stage/Host"
xtool dev build -c release
printf 'Built app: %s/Host/xtool/AtriaProbe.app\n' "$stage"
