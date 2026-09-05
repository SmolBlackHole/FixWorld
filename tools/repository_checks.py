"""Validate FixWorld repository hygiene and public documentation."""

from __future__ import annotations

import re
import subprocess
from collections import deque
from pathlib import Path
from urllib.parse import unquote


EXCLUDED_DIRECTORY_NAMES = frozenset(
    {
        ".git",
        ".mypy_cache",
        ".pytest_cache",
        ".ruff_cache",
        ".venv",
        "Assemblies",
        "bin",
        "dist",
        "node_modules",
        "obj",
        "temp",
        "TestResults",
    }
)
EXCLUDED_PREFIXES = (
    Path("data/profiling"),
    Path("data/benchmarks/results"),
    Path("data/benchmarks/saves"),
    Path("decompiled/Assembly-CSharp"),
    Path("decompiled/third-party"),
    Path("tools/dubs-performance-analyzer"),
    Path("tools/ilspycmd"),
)
EXCLUDED_FILES = frozenset({Path("mod/FixWorld/Local.Build.props")})
TEXT_FILENAMES = frozenset(
    {
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
        ".markdownlint.yml",
        "LICENSE",
    }
)
TEXT_SUFFIXES = frozenset(
    {
        ".cs",
        ".csproj",
        ".csv",
        ".example",
        ".json",
        ".md",
        ".props",
        ".ps1",
        ".py",
        ".slnx",
        ".txt",
        ".xml",
        ".yml",
        ".yaml",
    }
)
ALLOWED_TRACKED_BINARIES = frozenset(
    {
        Path("mod/FixWorld/Mods/FixWorld/Tools/Doorstop-4.4.0/winhttp.dll"),
        Path("mod/FixWorld/Mods/FixWorld/Tools/Windows-x64/texconv.exe"),
    }
)
FORBIDDEN_TRACKED_SUFFIXES = frozenset(
    {".7z", ".dll", ".exe", ".log", ".pdb", ".rar", ".rws", ".zip"}
)
MARKDOWN_LINK = re.compile(r"!?\[[^]]*]\(([^)]+)\)")
URL_SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*:")
LOCAL_WINDOWS_PATH = re.compile(
    r"\b[A-Za-z]:\\(?:Users|Projects|SteamLibrary|Steam)\\",
    re.IGNORECASE,
)
SECRET_PATTERNS = (
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    re.compile(r"\bghp_[A-Za-z0-9]{30,}\b"),
    re.compile(r"\bgithub_pat_[A-Za-z0-9_]{30,}\b"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
)


def _excluded(path: Path, root: Path) -> bool:
    relative = path.relative_to(root)
    if relative in EXCLUDED_FILES:
        return True
    if any(part in EXCLUDED_DIRECTORY_NAMES for part in relative.parts):
        return True
    return any(
        relative == prefix or prefix in relative.parents for prefix in EXCLUDED_PREFIXES
    )


def _text_files(root: Path) -> tuple[Path, ...]:
    files = (
        path
        for path in (root / relative for relative in _tracked_paths(root, include_untracked=True))
        if path.is_file()
        and not _excluded(path, root)
        and (path.name in TEXT_FILENAMES or path.suffix.lower() in TEXT_SUFFIXES)
    )
    return tuple(sorted(files))


def _markdown_pages(root: Path) -> tuple[Path, ...]:
    return tuple(
        path.relative_to(root)
        for path in _text_files(root)
        if path.suffix.lower() == ".md"
    )


def _markdown_targets(document: Path) -> tuple[str, ...]:
    targets: list[str] = []
    in_fence = False
    for line in document.read_text(encoding="utf-8").splitlines():
        if line.lstrip().startswith("```"):
            in_fence = not in_fence
        elif not in_fence:
            targets.extend(MARKDOWN_LINK.findall(line))
    if in_fence:
        raise ValueError(f"unclosed Markdown fence: {document}")
    return tuple(targets)


def _local_markdown_target(root: Path, source: Path, raw_target: str) -> Path | None:
    target = raw_target.strip()
    if target.startswith("<"):
        closing = target.find(">")
        if closing == -1:
            return None
        target = target[1:closing]
    else:
        target = target.partition(" ")[0]
    if not target or target.startswith("#") or URL_SCHEME.match(target):
        return None
    target = unquote(target.partition("#")[0])
    if not target:
        return None
    candidate = (root / source.parent / target).resolve()
    if candidate.is_dir():
        candidate /= "README.md"
    if candidate.suffix.lower() != ".md":
        return None
    try:
        return candidate.relative_to(root.resolve())
    except ValueError:
        return Path("..") / candidate.name


def _check_markdown_graph(root: Path) -> list[str]:
    pages = _markdown_pages(root)
    page_set = frozenset(pages)
    edges: dict[Path, set[Path]] = {page: set() for page in pages}
    errors: list[str] = []

    for source in pages:
        for raw_target in _markdown_targets(root / source):
            target = _local_markdown_target(root, source, raw_target)
            if target is None:
                continue
            if target not in page_set:
                errors.append(f"broken Markdown link: {source} -> {raw_target}")
            else:
                edges[source].add(target)

    root_document = Path("README.md")
    if root_document not in page_set:
        return [*errors, "missing documentation root: README.md"]

    reachable = {root_document}
    queue = deque((root_document,))
    while queue:
        source = queue.popleft()
        for target in edges[source]:
            if target not in reachable:
                reachable.add(target)
                queue.append(target)
    for page in sorted(page_set - reachable):
        errors.append(f"unreachable Markdown page: {page}")
    return errors


def _check_text_files(root: Path) -> list[str]:
    errors: list[str] = []
    for path in _text_files(root):
        contents = path.read_bytes()
        relative = path.relative_to(root)
        if contents and not contents.endswith(b"\n"):
            errors.append(f"missing final newline: {relative}")
        # Git normalizes tracked text to LF; a Windows checkout may contain CRLF.
        try:
            text = contents.decode("utf-8")
        except UnicodeDecodeError:
            errors.append(f"not UTF-8: {relative}")
            continue
        if LOCAL_WINDOWS_PATH.search(text):
            errors.append(f"local Windows path in tracked text: {relative}")
        if any(pattern.search(text) for pattern in SECRET_PATTERNS):
            errors.append(f"possible secret in tracked text: {relative}")
    return errors


def _tracked_paths(root: Path, include_untracked: bool = False) -> tuple[Path, ...]:
    completed = subprocess.run(
        ("git", "ls-files", "-z", "--cached", *(('--others', '--exclude-standard') if include_untracked else ())),
        cwd=root,
        check=True,
        capture_output=True,
    )
    return tuple(
        Path(raw.decode("utf-8")) for raw in completed.stdout.split(b"\0") if raw
    )


def _check_tracked_artifacts(root: Path) -> list[str]:
    errors: list[str] = []
    for path in _tracked_paths(root):
        if path in ALLOWED_TRACKED_BINARIES:
            continue
        if path.suffix.lower() in FORBIDDEN_TRACKED_SUFFIXES:
            errors.append(f"forbidden tracked artifact: {path}")
        if any(part in {"Assemblies", "bin", "obj"} for part in path.parts):
            errors.append(f"forbidden tracked build output: {path}")
        if any(
            path == prefix or prefix in path.parents for prefix in EXCLUDED_PREFIXES
        ):
            errors.append(f"forbidden tracked private data: {path}")
    return errors


def _check_git_diff(root: Path) -> list[str]:
    if not (root / ".git").exists():
        return []
    completed = subprocess.run(
        ("git", "diff", "--check"),
        cwd=root,
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode == 0:
        return []
    details = (completed.stdout + completed.stderr).strip()
    return [f"git diff --check failed: {details}"]


def repository_errors(root: Path) -> tuple[str, ...]:
    """Return all deterministic repository validation failures."""
    resolved = root.resolve()
    return tuple(
        [
            *_check_text_files(resolved),
            *_check_markdown_graph(resolved),
            *_check_tracked_artifacts(resolved),
            *_check_git_diff(resolved),
        ]
    )


def run_repository_checks(root: Path) -> None:
    """Print a compact result and exit on validation failures."""
    errors = repository_errors(root)
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        raise SystemExit(1)
    print("Repository checks passed.")


if __name__ == "__main__":
    run_repository_checks(Path(__file__).resolve().parents[1])
