# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Compare complete framehash streams without retaining decoded video."""

from __future__ import annotations

from collections.abc import Iterator
from fractions import Fraction
from itertools import zip_longest
from pathlib import Path


def frames(path: Path) -> Iterator[tuple[Fraction, Fraction, int, str]]:
    """Read FFmpeg framehash records with exact presentation clocks."""
    clock: Fraction | None = None
    with path.open(encoding="utf-8") as stream:
        for line in stream:
            if line.startswith("#tb 0:"):
                clock = Fraction(line.split(":", 1)[1].strip())
            elif line.strip() and not line.startswith("#"):
                parts = [part.strip() for part in line.split(",")]
                if clock is None or len(parts) != 6 or parts[0] != "0":
                    raise ValueError("Expected a single timed framehash video stream")
                digest = parts[5]
                if len(digest) != 64 or any(
                    c not in "0123456789abcdef" for c in digest
                ):
                    raise ValueError("Expected a SHA-256 frame hash")
                yield int(parts[2]) * clock, int(parts[3]) * clock, int(
                    parts[4]
                ), digest


def compare_frames(left: Path, right: Path) -> dict[str, int | str]:
    """Check every frame's visible bytes, presentation time and duration."""
    count = different = timings = total = 0
    for first, second in zip_longest(frames(left), frames(right)):
        if first is None or second is None:
            raise ValueError("Decoded frame counts differ")
        if first[2] != second[2]:
            raise ValueError("Decoded frame sizes differ")
        different += first[3] != second[3]
        timings += first[:2] != second[:2]
        count += 1
        total += first[2]
    if count == 0:
        raise ValueError("Decoded frame stream is empty")
    return {
        "representation": "native YUV420P8 SHA-256 per frame",
        "frames": count,
        "decoded_bytes": total,
        "different_frame_hashes": different,
        "different_frame_timings": timings,
    }
