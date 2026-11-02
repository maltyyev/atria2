#!/usr/bin/env bash
set -euo pipefail

tools_root="$(cd -- "$(dirname -- "$0")" && pwd -P)"
repo_root="$(cd -- "$tools_root/../.." && pwd -P)"
export PATH="/opt/atria-ios/swift/usr/bin:$PATH"

stage="$(python3 "$tools_root/stage_paths.py" "$repo_root" "${ATRIA_STAGE:-}")"

export ATRIA_NATIVEAOT_ROOT="${ATRIA_NATIVEAOT_ROOT:-$repo_root/.packages/microsoft.netcore.app.runtime.nativeaot.ios-arm64/10.0.13/runtimes/ios-arm64/native}"
host_source="$repo_root/Src/Hosts/Ios/Swift/Sources"
platform_source="$repo_root/Src/Platforms/Ios/Swift/Sources/AtriaProbe"
generated_source_resources="$host_source/AtriaProbe/Resources"
if [[ -e "$generated_source_resources" ]]; then
  printf 'Generated resources must not live in the Swift source tree: %s\n' "$generated_source_resources" >&2
  exit 1
fi

asset_root="$stage/Resources/assets"
if [[ ! -f "$asset_root/manifest.json" ]]; then
  printf 'Run Build/Ios/Prepare-Assets.py first; staged manifest is missing: %s\n' "$asset_root/manifest.json" >&2
  exit 1
fi
if [[ ! -f "$stage/Game.ios-arm64.o" ]]; then
  printf 'Run Build/Ios/Compile-Object.ps1 first; NativeAOT object is missing: %s\n' "$stage/Game.ios-arm64.o" >&2
  exit 1
fi

assert_stage_output() {
  local output="$1" resolved
  if [[ -L "$output" ]]; then
    printf 'Refusing linked staging output: %s\n' "$output" >&2
    exit 1
  fi
  resolved="$(realpath -m -- "$output")"
  case "$resolved" in
    "$stage"/*) ;;
    *) printf 'Refusing staging output outside ATRIA_STAGE: %s\n' "$resolved" >&2; exit 1 ;;
  esac
}

mkdir -p -- "$stage"
staged_sources="$stage/Host/Sources"
for output in "$stage/Host" "$staged_sources" "$stage/Host/.build" "$stage/Host/.bsp" \
              "$stage/Host/.swiftpm" "$stage/Host/xtool" "$stage/Host/xtool/.xtool-tmp"; do
  assert_stage_output "$output"
done
resolved_staged_sources="$(realpath -m -- "$staged_sources")"

mkdir -p -- "$stage/Host"
# Rebuild only the generated Swift source copy after proving it resolves under the isolated stage.
rm -rf -- "$resolved_staged_sources"
cp -a -- "$host_source" "$stage/Host/"
mkdir -p -- "$stage/Host/Sources/AtriaProbe"
cp -a -- "$platform_source/." "$stage/Host/Sources/AtriaProbe/"
mkdir -p -- "$stage/Host/Sources/AtriaProbe/Resources"
cp -a -- "$asset_root" "$stage/Host/Sources/AtriaProbe/Resources/"
cp -- "$repo_root/Src/Hosts/Ios/Swift/Package.swift" \
  "$repo_root/Src/Hosts/Ios/Swift/xtool.yml" \
  "$repo_root/Src/Hosts/Ios/Swift/Game-Info.plist" "$stage/Host/"

cd -- "$stage/Host"
xtool dev build -c release
printf 'Built app: %s/Host/xtool/AtriaProbe.app\n' "$stage"
