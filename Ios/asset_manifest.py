"""Expand the MSBuild resource inventory for iOS staging and its checks."""
from pathlib import Path
import xml.etree.ElementTree as ET


def listed_files(props: ET.Element, assets: Path, item_name: str) -> list[Path]:
    """Resolve one inventory item; an explicit missing resource is an error."""
    files = []
    for group in props.findall('ItemGroup'):
        for item in group.findall(item_name):
            for include in item.attrib['Include'].split(';'):
                value = include.strip().replace('$(MSBuildThisFileDirectory)', '')
                relative = Path(value)
                if relative.is_absolute() or '..' in relative.parts:
                    raise ValueError(f'Invalid {item_name} path: {value}')
                if '*' in value:
                    matches = [path for path in sorted(assets.glob(value)) if path.is_file()]
                    if not matches:
                        raise FileNotFoundError(f'{item_name} glob matched no files: {value}')
                    files.extend(matches)
                else:
                    source = assets / relative
                    if not source.is_file():
                        raise FileNotFoundError(f'Missing {item_name}: {source}')
                    files.append(source)
    if not files:
        raise ValueError(f'No items declared for {item_name}')
    return files


def verify_exact_inventory(folder: Path, files: list[Path], item_name: str) -> None:
    """Reject both missing declared files and unlisted files in immutable folders."""
    declared = {path.relative_to(folder) for path in files}
    present = {path.relative_to(folder) for path in folder.rglob('*') if path.is_file()}
    missing, unlisted = declared - present, present - declared
    if missing or unlisted:
        raise RuntimeError(f'{item_name} inventory mismatch: missing={sorted(map(str, missing))}, unlisted={sorted(map(str, unlisted))}')
