# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Create deterministic vector and JPEG/soft-mask PDF page workloads."""

from __future__ import annotations

import argparse
import zlib
from pathlib import Path

from PIL import Image


def stream(dictionary: bytes, payload: bytes) -> bytes:
    """Serialize one bounded PDF stream with an explicit byte length."""
    if dictionary:
        dictionary += b" "
    return (
        b"<< "
        + dictionary
        + f"/Length {len(payload)} >>\nstream\n".encode()
        + payload
        + b"\nendstream"
    )


def write_pdf(
    path: Path, contents: bytes, resources: bytes, extra: list[bytes]
) -> None:
    """Write a one-page PDF with an ordinary cross-reference table."""
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> "
        + resources
        + b">> /Contents 5 0 R >>",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        stream(b"", contents),
        *extra,
    ]
    data = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets: list[int] = []
    for number, value in enumerate(objects, 1):
        offsets.append(len(data))
        data.extend(f"{number} 0 obj\n".encode() + value + b"\nendobj\n")
    start = len(data)
    data.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode())
    for offset in offsets:
        data.extend(f"{offset:010d} 00000 n \n".encode())
    data.extend(
        f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n".encode()
    )
    path.write_bytes(data)


def prepare(output: Path, photo: Path) -> None:
    """Generate a vector/text page and a full-resolution photograph with a soft mask."""
    vector = [
        "1 1 1 rg 0 0 612 792 re f\n",
        "0 0 0 rg BT /F1 18 Tf 36 748 Td (Media format benchmark) Tj ET\n",
    ]
    for row in range(30):
        for column in range(18):
            red = ((row * 18 + column) % 17) / 20
            vector.append(
                f"{red:.2f} {1 - red:.2f} 0.4 rg {36 + column * 30} {55 + row * 22} 27 19 re f\n"
            )
    write_pdf(
        output / "vector-page.pdf", "".join(vector).rstrip("\n").encode(), b"", []
    )
    with Image.open(photo) as image:
        if image.format != "JPEG" or image.mode != "RGB":
            raise ValueError("Expected an RGB JPEG photograph")
        width, height = image.size
    if height < 2:
        raise ValueError("The soft-mask workload requires at least two rows")
    mask = b"".join(bytes([row * 255 // (height - 1)]) * width for row in range(height))
    dimensions = (
        f"/Type /XObject /Subtype /Image /Width {width} /Height {height}".encode()
    )
    photograph = stream(
        dimensions
        + b" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /SMask 7 0 R",
        photo.read_bytes(),
    )
    soft_mask = stream(
        dimensions
        + b" /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode",
        zlib.compress(mask),
    )
    content = b"1 1 1 rg 0 0 612 792 re f\nq 540 0 0 540 36 126 cm /Im0 Do Q"
    write_pdf(
        output / "scan-page.pdf",
        content,
        b"/XObject << /Im0 6 0 R >> ",
        [photograph, soft_mask],
    )


def main() -> None:
    """Write both authored page inputs without font or image substitution."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--photo", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    prepare(args.output, args.photo)


if __name__ == "__main__":
    main()
