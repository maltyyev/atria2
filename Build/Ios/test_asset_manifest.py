"""Check manifest expansion, required explicit files and exact asset membership."""
from pathlib import Path
import os
import subprocess
from tempfile import TemporaryDirectory
import xml.etree.ElementTree as ET

from asset_manifest import listed_files, verify_exact_inventory
from stage_paths import ensure_no_links, resolve_stage


def make_directory_link(link: Path, target: Path) -> None:
    try:
        link.symlink_to(target, target_is_directory=True)
    except OSError:
        if os.name != 'nt':
            raise
        subprocess.run(['cmd.exe', '/c', 'mklink', '/J', str(link), str(target)], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def remove_directory_link(link: Path) -> None:
    if os.name == 'nt':
        link.rmdir()
    else:
        link.unlink()

repo = Path(__file__).resolve().parents[2]
assert resolve_stage(repo) == repo / 'artifacts' / 'stage' / 'ios'

with TemporaryDirectory(dir=repo.parent) as temporary:
    temporary = Path(temporary)
    external_stage = temporary / 'external-stage'
    assert resolve_stage(repo, external_stage) == external_stage

    for unsafe in ('relative-stage', repo, repo.parent):
        try:
            resolve_stage(repo, unsafe)
            raise AssertionError(f'unsafe stage was accepted: {unsafe}')
        except ValueError:
            pass

    source_alias = temporary / 'source-alias'
    make_directory_link(source_alias, repo / 'Src')
    try:
        resolve_stage(repo, source_alias / 'stage')
        raise AssertionError('a stage path through a source alias was accepted')
    except ValueError:
        pass
    remove_directory_link(source_alias)

    stage = temporary / 'contained-stage'
    linked_sources = stage / 'Host' / 'Sources'
    linked_sources.parent.mkdir(parents=True)
    make_directory_link(linked_sources, stage)
    try:
        ensure_no_links(linked_sources, stage)
        raise AssertionError('a generated-output cleanup link was accepted')
    except ValueError:
        pass
    remove_directory_link(linked_sources)

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
