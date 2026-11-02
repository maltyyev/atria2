"""Stage the shared immutable asset inventory and iOS-only movie conversions."""
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from asset_manifest import listed_files, verify_exact_inventory

repo = Path(__file__).resolve().parents[1]
assets = repo / 'Assets'
destination = repo / 'Src/Hosts/Ios/Swift/Sources/AtriaProbe/Resources/assets'
destination.mkdir(parents=True, exist_ok=True)
props = ET.parse(assets / 'PackageResources.props').getroot()
manifest = {}


def digest(path):
    """Return a file's SHA-256 for staged-copy and conversion invalidation checks."""
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


for item_name, folder in (
    ('GameResource', 'Game'), ('MusicResource', 'Music'),
    ('InterfaceResource', 'Interface'), ('LocalizationResource', 'Localization')
):
    source_root = assets / folder
    files = listed_files(props, assets, item_name)
    if item_name in ('GameResource', 'MusicResource'):
        verify_exact_inventory(source_root, files, item_name)

    for source in files:
        relative = Path(folder.lower()) / source.relative_to(source_root)
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        expected = digest(source)
        if not target.exists() or digest(target) != expected:
            shutil.copyfile(source, target)
        if digest(target) != expected:
            raise RuntimeError(f'Staged resource hash mismatch: {relative}')
        manifest[relative.as_posix()] = expected

ffmpeg = repo / 'third_party/ffmpeg/ffmpeg-n9.0.2-33-g4cf298e66a-win64-lgpl-shared-9.0/bin/ffmpeg.exe'
movies = destination / 'movies'
movies.mkdir(exist_ok=True)
for source in (path for path in listed_files(props, assets, 'GameResource') if path.suffix.lower() == '.jad'):
    target = movies / (source.stem + '.mp4')
    stamp = movies / (source.stem + '.sha256')
    expected = digest(source)
    if not target.exists() or not stamp.exists() or stamp.read_text(encoding='utf-8') != expected:
        temporary = target.with_suffix('.tmp.mp4')
        subprocess.run([str(ffmpeg), '-hide_banner', '-nostdin', '-y', '-i', str(source),
            '-map', '0:v:0', '-map', '0:a:0?', '-c:v', 'h264_mf', '-b:v', '4000k',
            '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', str(temporary)], check=True)
        temporary.replace(target)
        stamp.write_text(expected, encoding='utf-8')
    manifest[target.relative_to(destination).as_posix()] = digest(target)

(destination / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(f'Verified {len(manifest)} staged resources; player data excluded.')
