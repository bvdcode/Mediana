"""Reconstruct centered full-range JPEG reference pixels from decoded planar samples."""

from dataclasses import dataclass
from math import floor
from pathlib import Path


@dataclass(frozen=True)
class Sampling:
    """JPEG chroma sampling factors relative to luma."""

    name: str
    horizontal: int
    vertical: int


def interpolate(
    plane: bytes, width: int, height: int, x: float, y: float
) -> float:
    """Bilinearly reconstruct a component with replicated edges."""
    x = max(0.0, min(width - 1.0, x))
    y = max(0.0, min(height - 1.0, y))
    left, top = floor(x), floor(y)
    right, bottom = min(left + 1, width - 1), min(top + 1, height - 1)
    fx, fy = x - left, y - top
    upper = plane[top * width + left] * (1 - fx) + plane[top * width + right] * fx
    lower = (
        plane[bottom * width + left] * (1 - fx) + plane[bottom * width + right] * fx
    )
    return upper * (1 - fy) + lower * fy


def reconstruct(root: Path, sampling: Sampling, width: int, height: int) -> None:
    """Write complete RGBA references using floating-point BT.601 equations."""
    cw, ch = width // sampling.horizontal, height // sampling.vertical
    luma_size, chroma_size = width * height, cw * ch
    frame_size = luma_size + chroma_size * 2
    with (root / f"mjpeg-{sampling.name}.yuv").open("rb") as source:
        with (root / f"mjpeg-{sampling.name}.rgba").open("wb") as destination:
            while encoded := source.read(frame_size):
                if len(encoded) != frame_size:
                    raise ValueError("The planar reference has a truncated picture")
                luma = encoded[:luma_size]
                cb = encoded[luma_size : luma_size + chroma_size]
                cr = encoded[luma_size + chroma_size :]
                output = bytearray()
                for y in range(height):
                    for x in range(width):
                        cx = (x - (sampling.horizontal - 1) / 2) / sampling.horizontal
                        cy = (y - (sampling.vertical - 1) / 2) / sampling.vertical
                        b = interpolate(cb, cw, ch, cx, cy) - 128
                        r = interpolate(cr, cw, ch, cx, cy) - 128
                        value = luma[y * width + x]
                        red = value + 1.402 * r
                        green = value - 0.34413628620102216 * b - 0.7141362862010221 * r
                        blue = value + 1.772 * b
                        output.extend(
                            [max(0, min(255, floor(channel + 0.5))) for channel in (red, green, blue)]
                        )
                        output.append(255)
                destination.write(output)


if __name__ == "__main__":
    for item in (Sampling("420", 2, 2), Sampling("422", 2, 1), Sampling("444", 1, 1)):
        reconstruct(Path(__file__).parent, item, 64, 48)
