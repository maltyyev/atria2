#!/usr/bin/env bash
set -euo pipefail
ios_root="$(cd -- "$(dirname -- "$0")" && pwd)"
export PATH="/opt/atria-ios/swift/usr/bin:$PATH"
export ATRIA_NATIVEAOT_ROOT="$ios_root/../.packages/microsoft.netcore.app.runtime.nativeaot.ios-arm64/10.0.13/runtimes/ios-arm64/native"
stage=/opt/atria-ios/game-host
mkdir -p "$stage/Host"
# Foundation copies in small chunks; stage on Linux storage before xtool packages large assets.
cp -a "$ios_root/Host/Sources" "$stage/Host/"
cp "$ios_root/Host/Package.swift" "$ios_root/Host/xtool.yml" "$ios_root/Host/Game-Info.plist" "$stage/Host/"
cp "$ios_root/Game.ios-arm64.o" "$stage/"
cd "$stage/Host"
xtool dev build -c release
printf 'Built app: %s/Host/xtool/AtriaProbe.app\n' "$stage"
