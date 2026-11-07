# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Decode JPEG/WebP with native Pillow codecs and write an RGBA8 PNG."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def main() -> None:
    """Include process startup, input loading, reconstruction and PNG encoding."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError(args.output)
    with Image.open(args.input) as source:
        if source.format not in {"JPEG", "WEBP"}:
            raise ValueError("Expected a JPEG or WebP reference input")
        image = source.convert("RGBA")
        image.save(
            args.output,
            format="PNG",
            compress_level=6,
            icc_profile=source.info.get("icc_profile"),
        )


if __name__ == "__main__":
    main()
