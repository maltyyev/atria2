"""Check the unsigned probe's package and actual iPhone executable, not runtime behavior."""
from pathlib import Path
import plistlib
import json
import struct
import sys
import zipfile

app = Path(sys.argv[1])
info = plistlib.loads((app / 'Info.plist').read_bytes())
assert info['CFBundleIdentifier'] == 'com.maltyyev.atria.probe'
assert 'iPhoneOS' in info['CFBundleSupportedPlatforms']
data = (app / info['CFBundleExecutable']).read_bytes()
magic, cpu, subtype, kind, count, commands_size, flags, reserved = struct.unpack_from('<8I', data)
assert (magic, cpu, kind) == (0xFEEDFACF, 0x0100000C, 2), 'Expected ARM64 executable.'
offset = 32
platform = None
symbols = None
dependencies = []
for _ in range(count):
    command, size = struct.unpack_from('<2I', data, offset)
    assert size >= 8 and offset + size <= 32 + commands_size <= len(data)
    if command == 0x32:  # LC_BUILD_VERSION
        platform, minimum, sdk, tools = struct.unpack_from('<4I', data, offset + 8)
        assert platform == 2 and minimum <= (27 << 16), 'Must run on iOS 27.0.'
    if command == 2:  # LC_SYMTAB
        symbols = struct.unpack_from('<4I', data, offset + 8)
    if command in (0xC, 0x80000018, 0x8000001F):
        start = offset + struct.unpack_from('<I', data, offset + 8)[0]
        assert offset + 8 <= start < offset + size
        name = data[start:data.index(b'\0', start, offset + size)].decode()
        assert name.startswith(('/System/Library/', '/usr/lib/')), name
        dependencies.append(name)
    offset += size
assert offset == 32 + commands_size and platform == 2 and symbols is not None
table, entries, strings, strings_size = symbols
assert table + entries * 16 <= len(data) and strings + strings_size <= len(data)
defined = set()
for index in range(entries):
    name_offset, symbol_type, section, description, value = struct.unpack_from('<IBBHQ', data, table + index * 16)
    assert name_offset < strings_size
    if section and symbol_type & 0x0E == 0x0E:
        end = data.index(b'\0', strings + name_offset, strings + strings_size)
        defined.add(data[strings + name_offset:end].decode())
for symbol in ('_atria_start', '_atria_frame', '_atria_pad', '_atria_surface', '_apple_text_create', '_apple_sound_create', '_apple_movie_start', '_RhInitialize', '_RhSetRuntimeInitializationCallback'):
    assert symbol in defined, f'Missing C#/NativeAOT symbol: {symbol}'

import hashlib
assets = list(app.rglob('manifest.json'))
assert len(assets) == 1
root = assets[0].parent
manifest = json.loads(assets[0].read_text(encoding='utf-8'))
for relative, expected in manifest.items():
    with (root / relative).open('rb') as stream:
        assert hashlib.file_digest(stream, 'sha256').hexdigest() == expected, relative
assert not list(app.rglob('*.sav')), 'Player data must not be bundled.'
assert set(info['UISupportedInterfaceOrientations']) == {'UIInterfaceOrientationLandscapeLeft', 'UIInterfaceOrientationLandscapeRight'}
if len(sys.argv) > 2:
    with zipfile.ZipFile(sys.argv[2]) as archive:
        assert archive.testzip() is None
        assert archive.read(f'Payload/{app.name}/{info["CFBundleExecutable"]}') == data
print(f'PASS: ARM64 iOS executable {len(data)} bytes; shared game/runtime/Apple exports linked; {len(manifest)} resources verified.')
print('\n'.join(dependencies))
print('Physical gameplay/audio/controller behavior requires device validation.')
