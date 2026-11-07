# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Typed input and process measurements for complete media operations."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Command:
    """Executable arguments and explicit environment additions."""

    tool: str
    arguments: list[str]
    environment: dict[str, str]
    output: str


@dataclass(frozen=True)
class Workload:
    """One whole-file operation executed by both implementations."""

    id: str
    input: str
    output_extension: str
    description: str
    mediana: Command
    alternative: Command


@dataclass(frozen=True)
class Measurement:
    """Whole-process counters; validation is performed after timing."""

    case: str
    engine: str
    repetition: int
    elapsed_seconds: float
    cpu_seconds: float
    peak_rss_bytes: int
    exit_code: int
    timed_out: bool
    output_bytes: int
    output_sha256: str


def file_hash(path: Path) -> str:
    """Hash a file with bounded working memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def output_fingerprint(path: Path) -> tuple[int, str]:
    """Include each referenced segment when measuring a complete local HLS result."""
    if path.suffix != ".m3u8":
        return path.stat().st_size, file_hash(path)
    playlist = path.read_text(encoding="utf-8")
    lines = playlist.splitlines()
    if "#EXT-X-ENDLIST" not in lines:
        raise ValueError("Expected a finalized VOD playlist")
    root = path.parent.resolve()
    digest = hashlib.sha256(playlist.encode("utf-8"))
    size = path.stat().st_size
    segments = [line for line in lines if line and not line.startswith("#")]
    if not segments or len(segments) != len(set(segments)):
        raise ValueError("Expected distinct local VOD segments")
    for name in segments:
        segment = (root / name).resolve()
        if not segment.is_relative_to(root) or segment.suffix != ".ts":
            raise ValueError("Expected MPEG-TS segments within the output directory")
        size += segment.stat().st_size
        digest.update(name.encode("utf-8"))
        digest.update(bytes.fromhex(file_hash(segment)))
    return size, digest.hexdigest()


def load_workloads(path: Path) -> list[Workload]:
    """Read the explicit command manifest and reject ambiguous identities."""
    with path.open(encoding="utf-8") as stream:
        values = json.load(stream)
    workloads = [
        Workload(
            id=item["id"],
            input=item["input"],
            output_extension=item["output_extension"],
            description=item["description"],
            mediana=Command(**item["mediana"]),
            alternative=Command(**item["alternative"]),
        )
        for item in values
    ]
    if len({item.id for item in workloads}) != len(workloads):
        raise ValueError("Duplicate workload ID")
    for item in workloads:
        if not item.id or Path(item.id).name != item.id:
            raise ValueError("Workload ID must be a filename component")
        if Path(item.input).name != item.input:
            raise ValueError("Inputs must use filenames within the input directory")
        if not item.output_extension.startswith("."):
            raise ValueError("Output extension must begin with a dot")
        for command in (item.mediana, item.alternative):
            if not command.tool or not all(
                isinstance(arg, str) for arg in command.arguments
            ):
                raise ValueError("Commands must contain a tool and string arguments")
    return workloads
