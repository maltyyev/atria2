"""Check manifest expansion, required explicit files and exact asset membership."""
from pathlib import Path
from tempfile import TemporaryDirectory
import xml.etree.ElementTree as ET

from asset_manifest import listed_files, verify_exact_inventory

with TemporaryDirectory(dir=Path(__file__).resolve().parent) as temporary:
    assets = Path(temporary)
    game = assets / 'Game'
    interface = assets / 'Interface' / 'nested'
    game.mkdir()
    interface.mkdir(parents=True)
    explicit = game / 'core.dat'
    explicit.write_bytes(b'core')
    (interface / 'face.bmp').write_bytes(b'bmp')
    props = ET.fromstring('''<Project><ItemGroup>
      <GameResource Include="$(MSBuildThisFileDirectory)Game/core.dat" />
      <InterfaceResource Include="$(MSBuildThisFileDirectory)Interface/**/*.bmp" />
    </ItemGroup></Project>''')

    assert listed_files(props, assets, 'GameResource') == [explicit]
    globbed = listed_files(props, assets, 'InterfaceResource')
    assert [path.relative_to(assets / 'Interface').as_posix() for path in globbed] == ['nested/face.bmp']
    verify_exact_inventory(game, [explicit], 'GameResource')

    explicit.unlink()
    try:
        listed_files(props, assets, 'GameResource')
        raise AssertionError('missing explicit resource was accepted')
    except FileNotFoundError:
        pass

    explicit.write_bytes(b'core')
    (game / 'unlisted.dat').write_bytes(b'new')
    try:
        verify_exact_inventory(game, [explicit], 'GameResource')
        raise AssertionError('unlisted immutable resource was accepted')
    except RuntimeError:
        pass

print('Asset manifest checks passed.')
