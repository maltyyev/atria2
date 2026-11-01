"""Stage immutable game resources and iOS-readable cutscenes; never includes player data."""
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
destination = repo / 'Ios/Host/Sources/AtriaProbe/Resources/assets'
destination.mkdir(parents=True, exist_ok=True)
manifest = {}

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

for source_name, target_name, patterns in (
    ('Game', 'game', ['*']), ('Music', 'music', ['*.wav']),
    ('Interface', 'interface', ['*.bmp', '*.spr']), ('Localization', 'localization', ['**/*.json'])
):
    source = repo / 'Assets' / source_name
    for pattern in patterns:
        for file in sorted(source.glob(pattern)):
            if not file.is_file():
                continue
            relative = Path(target_name) / file.relative_to(source)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            expected = digest(file)
            if not target.exists() or digest(target) != expected:
                shutil.copyfile(file, target)
            assert digest(target) == expected, relative
            manifest[relative.as_posix()] = expected

ffmpeg = repo / 'third_party/ffmpeg/ffmpeg-n9.0.2-33-g4cf298e66a-win64-lgpl-shared-9.0/bin/ffmpeg.exe'
movies = destination / 'movies'
movies.mkdir(exist_ok=True)
for source in sorted((repo / 'Assets/Game').glob('*.jad')):
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
