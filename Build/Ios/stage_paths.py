"""Resolve iOS build output without allowing it to overlap repository sources."""
import os
from pathlib import Path
import stat


def _within(path: Path, parent: Path) -> bool:
    return path == parent or path.is_relative_to(parent)


def is_link(path: Path) -> bool:
    is_junction = getattr(path, 'is_junction', lambda: False)
    if path.is_symlink() or is_junction():
        return True
    try:
        attributes = path.lstat().st_file_attributes
    except (AttributeError, FileNotFoundError):
        return False
    return bool(attributes & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0x400))


def _absolute_without_links(path: Path) -> Path:
    absolute = Path(os.path.abspath(path))
    current = Path(absolute.anchor)
    for part in absolute.parts[1:]:
        current /= part
        if is_link(current):
            raise ValueError(f'Build output may not traverse a link: {current}')
    return absolute


def resolve_stage(repo_root: Path, configured: str | Path | None = None) -> Path:
    """Use artifacts/stage/ios by default and allow safe absolute overrides."""
    repo = repo_root.resolve()
    stage_root = repo / 'artifacts' / 'stage'
    stage_input = stage_root / 'ios' if configured is None else Path(configured)
    if configured is not None and not stage_input.is_absolute():
        raise ValueError('ATRIA_STAGE overrides must use an absolute path.')

    # Check every existing component before resolving so a junction cannot hide
    # a source directory behind an otherwise safe-looking stage path.
    stage = _absolute_without_links(stage_input)
    artifacts = repo / 'artifacts'
    if _absolute_without_links(artifacts) != artifacts or _absolute_without_links(stage_root) != stage_root:
        raise ValueError('artifacts/stage may not resolve through a link.')

    if stage == repo or _within(repo, stage):
        raise ValueError(f'ATRIA_STAGE may not be the repository or one of its parents: {stage}')
    if _within(stage, repo) and (stage == stage_root or not _within(stage, stage_root)):
        raise ValueError(f'ATRIA_STAGE inside the repository must be a child of artifacts/stage: {stage}')
    if stage.exists() and not stage.is_dir():
        raise ValueError(f'ATRIA_STAGE is not a directory: {stage}')
    return stage


def ensure_no_links(path: Path, boundary: Path) -> Path:
    """Return a contained output path after rejecting existing links in its path."""
    boundary = boundary.resolve()
    output = _absolute_without_links(path)
    if not _within(output, boundary):
        raise ValueError(f'Build output resolves outside its stage: {output}')
    return output


if __name__ == '__main__':
    import sys

    configured_stage = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] else None
    print(resolve_stage(Path(sys.argv[1]), configured_stage))
