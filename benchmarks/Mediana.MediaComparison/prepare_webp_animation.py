# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Encode a complete reference RGBA animation as a lossless WebP input."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def prepare(source: Path, output: Path, width: int, height: int, count: int) -> None:
    """Preserve every visible frame in a three-second, 60-frame-per-second cycle."""
    if width <= 0 or height <= 0 or count <= 0 or count % 3 != 0:
        raise ValueError(
            "Expected positive dimensions and complete groups of three frames"
        )
    frame_bytes = width * height * 4
    if source.stat().st_size != frame_bytes * count:
        raise ValueError("Unexpected RGBA stream length")
    frames: list[Image.Image] = []
    with source.open("rb") as stream:
        for _ in range(count):
            frames.append(
                Image.frombytes("RGBA", (width, height), stream.read(frame_bytes))
            )
    frames[0].save(
        output,
        save_all=True,
        append_images=frames[1:],
        lossless=True,
        method=4,
        quality=75,
        minimize_size=True,
        loop=0,
        duration=[16, 17, 17] * (count // 3),
    )
    for frame in frames:
        frame.close()


def main() -> None:
    """Read explicit reference-frame geometry and produce the benchmark input."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rgba", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--width", type=int, required=True)
    parser.add_argument("--height", type=int, required=True)
    parser.add_argument("--frames", type=int, required=True)
    args = parser.parse_args()
    prepare(args.rgba, args.output, args.width, args.height, args.frames)


if __name__ == "__main__":
    main()
