# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Generate deterministic RGB TIFF and PDF inputs using standard-library zlib."""
from __future__ import annotations

import argparse
import random
import struct
import zlib
from pathlib import Path


def pixels(width: int, height: int, noise: bool) -> bytes:
    """Create deterministic packed RGB samples."""
    if noise:
        return random.Random(741).randbytes(width * height * 3)
    return bytes(value for y in range(height) for x in range(width)
                 for value in (x % 256, y % 256, (x + y) % 256))


def tiff(width: int, height: int, rows: int, rgb: bytes) -> bytes:
    """Encode classic TIFF with independently compressed strips."""
    blocks = [zlib.compress(rgb[y * width * 3:min(y + rows, height) * width * 3])
              for y in range(0, height, rows)]
    entries = 10
    start = 8 + 2 + entries * 12 + 4
    bits = start
    offsets = bits + 6
    lengths = offsets + len(blocks) * 4
    payload = lengths + len(blocks) * 4
    block_offsets: list[int] = []
    for block in blocks:
        block_offsets.append(payload)
        payload += len(block)
    fields = [(256, 4, 1, width), (257, 4, 1, height), (258, 3, 3, bits),
              (259, 3, 1, 8), (262, 3, 1, 2),
              (273, 4, len(blocks), block_offsets[0] if len(blocks) == 1 else offsets),
              (277, 3, 1, 3), (278, 4, 1, rows),
              (279, 4, len(blocks), len(blocks[0]) if len(blocks) == 1 else lengths),
              (284, 3, 1, 1)]
    header = b'II' + struct.pack('<HIH', 42, 8, entries)
    return (header + b''.join(struct.pack('<HHII', *field) for field in fields)
            + struct.pack('<IHHH', 0, 8, 8, 8)
            + struct.pack(f'<{len(blocks)}I', *block_offsets)
            + struct.pack(f'<{len(blocks)}I', *(len(block) for block in blocks))
            + b''.join(blocks))


def pdf(size: int, images: int) -> bytes:
    """Build one PDF page containing independent Flate image objects."""
    content = b''.join(f'q {size} 0 0 {size} 0 0 cm /I{i} Do Q\n'.encode()
                       for i in range(images))
    resources = ' '.join(f'/I{i} {i + 5} 0 R' for i in range(images))
    compressed = zlib.compress(pixels(size, size, False))
    objects = [b'<< /Type /Catalog /Pages 2 0 R >>',
               b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
               (f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {size} {size}] '
                f'/Contents 4 0 R /Resources << /XObject << {resources} >> >> >>').encode(),
               b'<< /Length ' + str(len(content)).encode() + b' >>\nstream\n'
               + content + b'endstream']
    image = (f'<< /Type /XObject /Subtype /Image /Width {size} /Height {size} '
             f'/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode '
             f'/Length {len(compressed)} >>\nstream\n').encode() + compressed + b'\nendstream'
    objects.extend(image for _ in range(images))
    result = bytearray(b'%PDF-1.7\n')
    offsets = [0]
    for number, obj in enumerate(objects, 1):
        offsets.append(len(result))
        result.extend(f'{number} 0 obj\n'.encode() + obj + b'\nendobj\n')
    position = len(result)
    result.extend(f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode())
    for offset in offsets[1:]:
        result.extend(f'{offset:010d} 00000 n \n'.encode())
    result.extend((f'trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\n'
                   f'startxref\n{position}\n%%EOF\n').encode())
    return bytes(result)


def main() -> None:
    """Write the workload corpus to the requested directory."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory: Path = parser.parse_args().directory
    directory.mkdir(parents=True, exist_ok=True)
    for size in (64, 512, 1280):
        for noise in (False, True):
            rgb = pixels(size, size, noise)
            for rows in (1, 64):
                name = f'{size}-{"noise" if noise else "gradient"}-rows{rows}.tif'
                (directory / name).write_bytes(tiff(size, size, rows, rgb))
    (directory / 'images-small.pdf').write_bytes(pdf(128, 16))
    (directory / 'image-large.pdf').write_bytes(pdf(1024, 1))


if __name__ == '__main__':
    main()
