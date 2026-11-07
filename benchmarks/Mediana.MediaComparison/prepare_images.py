# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Prepare deterministic image and page workloads from an explicit source photograph."""

from __future__ import annotations

import argparse
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, TiffImagePlugin


def segment(number: int, kind: int, page: int, payload: bytes) -> bytes:
    """Serialize a JBIG2 segment without referred-to segments."""
    return struct.pack(">IBBBI", number, kind, 0, page, len(payload)) + payload


def prepare_scan(destination: Path, font_path: Path) -> None:
    """Create a 2048 by 3072 monochrome page and its independent MMR codestream."""
    width, height = 2048, 3072
    page = Image.new("1", (width, height), 1)
    draw = ImageDraw.Draw(page)
    font = ImageFont.truetype(str(font_path), 26)
    for row in range(76):
        y = 45 + row * 38
        draw.text(
            (55, y),
            f"{row + 1:03d}  Document benchmark: text, rules and repeated symbols 0123456789",
            fill=0,
            font=font,
        )
        if row % 8 == 0:
            draw.line((45, y + 32, width - 45, y + 32), fill=0, width=2)
    encoded = destination / "scan-group4.tiff"
    page.save(encoded, compression="group4", tiffinfo={278: height})
    data = encoded.read_bytes()
    with Image.open(encoded) as tiff:
        if not isinstance(tiff, TiffImagePlugin.TiffImageFile):
            raise TypeError("Expected a TIFF image")
        offsets = tiff.tag_v2[273]
        lengths = tiff.tag_v2[279]
        rows_per_strip = int(tiff.tag_v2[278])
    parts = [segment(1, 48, 1, struct.pack(">IIII B H", width, height, 0, 0, 0, 0))]
    top = 0
    for number, (offset, length) in enumerate(zip(offsets, lengths), start=2):
        region_height = min(rows_per_strip, height - top)
        region = struct.pack(">IIII BB", width, region_height, 0, top, 0, 1)
        parts.append(segment(number, 38, 1, region + data[offset : offset + length]))
        top += region_height
    parts.append(segment(len(parts) + 1, 49, 1, b""))
    parts.append(segment(len(parts) + 1, 51, 0, b""))
    (destination / "scan-mmr.jb2").write_bytes(
        b"\x97JB2\r\n\x1a\n\x03" + b"".join(parts)
    )
    page.save(destination / "scan-source.png")


def prepare_artwork(destination: Path) -> None:
    """Create a large vector scene with gradients, clipping, strokes and blur."""
    elements = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="2560" height="2560" viewBox="0 0 2560 2560">',
        '<defs><linearGradient id="g"><stop stop-color="#17528c"/><stop offset="1" stop-color="#eea84f"/></linearGradient>',
        '<clipPath id="clip"><circle cx="1280" cy="1280" r="1210"/></clipPath>',
        '<filter id="blur"><feGaussianBlur stdDeviation="7"/></filter></defs>',
        '<rect width="2560" height="2560" fill="#f5f2e9"/><g clip-path="url(#clip)">',
    ]
    for index in range(180):
        x, y = (index * 337) % 2500, (index * 541) % 2500
        radius = 35 + index % 91
        elements.append(
            f'<circle cx="{x}" cy="{y}" r="{radius}" fill="url(#g)" opacity="0.75" stroke="#17334f" stroke-width="3"/>'
        )
    elements.extend(
        [
            '</g><circle cx="1280" cy="1280" r="170" fill="#cc4433" filter="url(#blur)"/></svg>'
        ]
    )
    (destination / "vector-scene.svg").write_text("\n".join(elements), encoding="utf-8")


def main() -> None:
    """Preserve source geometry while exporting independently readable formats."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--photo", type=Path, required=True)
    parser.add_argument("--font", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    with Image.open(args.photo) as source:
        rgb = source.convert("RGB")
        rgb.save(args.output / "photo-baseline.jpg", quality=92, subsampling=2)
        rgb.save(
            args.output / "photo-progressive.jpg",
            quality=92,
            subsampling=2,
            progressive=True,
        )
        rgb.save(args.output / "photo-source.ppm")
        rgb.save(args.output / "photo-lzw.tiff", compression="tiff_lzw")
        rgba = rgb.convert("RGBA")
        alpha = Image.linear_gradient("L").resize(rgb.size)
        rgba.putalpha(alpha)
        rgba.save(args.output / "large-alpha.png")
    prepare_scan(args.output, args.font)
    prepare_artwork(args.output)


if __name__ == "__main__":
    main()
