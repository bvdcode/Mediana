# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Describe the measured executable and runtime without machine-specific paths."""

from __future__ import annotations

import hashlib
import json
import os
import platform
import subprocess
from datetime import datetime, timezone
from pathlib import Path


def command_output(command: list[str]) -> str:
    """Read successful command output without including diagnostic environment data."""
    return subprocess.run(
        command, capture_output=True, text=True, check=True
    ).stdout.strip()


def metadata(dotnet: Path, binary: Path, suite: str) -> dict[str, object]:
    """Capture executable hashes, versions, workload identity, and CPU limits."""
    dependency_path = binary.with_suffix(".deps.json")
    dependencies = json.loads(dependency_path.read_text(encoding="utf-8"))
    cpu_model = platform.machine()
    cpu_info = Path("/proc/cpuinfo")
    if cpu_info.exists():
        for line in cpu_info.read_text(encoding="utf-8").splitlines():
            if line.startswith("model name"):
                cpu_model = line.partition(":")[2].strip()
                break
    quota = Path("/sys/fs/cgroup/cpu.max")
    return {
        "started_at": datetime.now(timezone.utc).isoformat(),
        "suite": suite,
        "source_commit": command_output(["git", "rev-parse", "HEAD"]),
        "source_dirty": bool(command_output(["git", "status", "--porcelain"])),
        "binary_sha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
        "drawing_sha256": hashlib.sha256(
            (binary.parent / "Mediana.Drawing.dll").read_bytes()
        ).hexdigest(),
        "dotnet_sdk": command_output([str(dotnet), "--version"]),
        "runtimes": [
            " ".join(line.split()[:2])
            for line in command_output([str(dotnet), "--list-runtimes"]).splitlines()
        ],
        "reference_packages": sorted(
            name for name in dependencies["libraries"] if name.startswith("SixLabors.")
        ),
        "os": platform.system(),
        "architecture": platform.machine(),
        "cpu_model": cpu_model,
        "cpu_quota": (
            quota.read_text(encoding="utf-8").strip()
            if quota.exists()
            else "unavailable"
        ),
        "available_cpus": (
            len(os.sched_getaffinity(0))
            if hasattr(os, "sched_getaffinity")
            else os.cpu_count()
        ),
        "tiered_compilation": False,
    }
