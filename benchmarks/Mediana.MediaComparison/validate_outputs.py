# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Compare complete decoded samples with bounded validation scratch memory."""

from __future__ import annotations

import argparse
import io
import json
import math
import wave
from collections.abc import Callable
from pathlib import Path

import numpy as np
from frame_validation import compare_frames
from media_validation import compare_media
from PIL import Image, ImageCms


def normalized_image(image: Image.Image) -> Image.Image:
    """Compare declared ICC colors in sRGB, preserving straight alpha."""
    profile = image.info.get("icc_profile")
    if profile is not None:
        converted = ImageCms.profileToProfile(
            image.convert("RGBA"),
            ImageCms.ImageCmsProfile(io.BytesIO(profile)),
            ImageCms.createProfile("sRGB"),
            outputMode="RGBA",
        )
        if not isinstance(converted, Image.Image):
            raise TypeError("Color conversion did not return an image")
        return converted
    return image.convert("RGBA")


def compare_samples(
    read_left: Callable[[int], bytes], read_right: Callable[[int], bytes], dtype: str
) -> dict[str, int | float]:
    """Report exact agreement and error without loading complete decoded files."""
    count = different = maximum = squares = 0
    while True:
        first = read_left(262144)
        second = read_right(262144)
        if len(first) != len(second):
            raise ValueError("Decoded sample lengths differ")
        if not first:
            break
        difference = np.frombuffer(first, dtype=dtype).astype(np.int32)
        difference -= np.frombuffer(second, dtype=dtype)
        count += difference.size
        different += int(np.count_nonzero(difference))
        maximum = max(maximum, int(np.max(np.abs(difference))))
        squares += int(np.sum(difference.astype(np.int64) ** 2))
    if count == 0:
        raise ValueError("Decoded output is empty")
    return {
        "samples": count,
        "different_samples": different,
        "max_absolute_error": maximum,
        "rmse": math.sqrt(squares / count),
    }


def validate_pair(
    left: Path, right: Path, source: Path | None = None
) -> dict[str, int | float | str]:
    """Compare raw RGBA bytes or PCM16 WAVE data, excluding container metadata."""
    if left.suffix in {".png", ".webp", ".ppm", ".pgm"}:
        with Image.open(left) as first_image, Image.open(right) as second_image:
            if first_image.size != second_image.size:
                raise ValueError(
                    f"Image dimensions differ: {first_image.size} vs {second_image.size}"
                )
            first_rgba = normalized_image(first_image)
            second_rgba = normalized_image(second_image)
            count = different = maximum = squares = 0
            for top in range(0, first_rgba.height, 64):
                box = (0, top, first_rgba.width, min(top + 64, first_rgba.height))
                difference = np.asarray(first_rgba.crop(box)).astype(np.int16)
                difference -= np.asarray(second_rgba.crop(box))
                count += difference.size
                different += int(np.count_nonzero(difference))
                maximum = max(maximum, int(np.max(np.abs(difference))))
                squares += int(np.sum(difference.astype(np.int64) ** 2))
            metrics: dict[str, int | float | str] = {
                "representation": "decoded sRGB RGBA8; declared ICC normalized",
                "width": first_rgba.width,
                "height": first_rgba.height,
                "samples": count,
                "different_samples": different,
                "max_absolute_error": maximum,
                "rmse": math.sqrt(squares / count),
            }
            if left.suffix == ".webp" and source is not None:
                metrics["mediana_source_rmse"] = validate_pair(source, left)["rmse"]
                metrics["alternative_source_rmse"] = validate_pair(source, right)[
                    "rmse"
                ]
            return metrics
    if left.suffix == ".framehash":
        return {**compare_frames(left, right)}
    if left.suffix in {".ts", ".m3u8"}:
        if source is None:
            raise ValueError("Original input is required for transport validation")
        return compare_media(left, right, source)
    if left.suffix in {".rgba", ".pcm", ".bin"}:
        with left.open("rb") as first, right.open("rb") as second:
            return {
                "representation": "PCM16LE" if left.suffix == ".pcm" else "raw bytes",
                **compare_samples(
                    first.read, second.read, "<i2" if left.suffix == ".pcm" else "u1"
                ),
            }
    if left.suffix == ".wav":
        with wave.open(str(left), "rb") as first, wave.open(str(right), "rb") as second:
            first_format = first.getparams()[:4]
            if first_format != second.getparams()[:4] or first.getsampwidth() != 2:
                raise ValueError(
                    "Expected matching PCM16 WAVE formats and sample counts"
                )
            return {
                "representation": "PCM16LE",
                "channels": first.getnchannels(),
                "sample_rate": first.getframerate(),
                "frames": first.getnframes(),
                **compare_samples(first.readframes, second.readframes, "<i2"),
            }
    raise ValueError(f"No sample validator for {left.suffix}")


def main() -> None:
    """Validate in a separate process so numerical imports do not affect measured RSS."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("left", type=Path)
    parser.add_argument("right", type=Path)
    parser.add_argument("--source", type=Path)
    args = parser.parse_args()
    print(json.dumps(validate_pair(args.left, args.right, args.source)))


if __name__ == "__main__":
    main()
