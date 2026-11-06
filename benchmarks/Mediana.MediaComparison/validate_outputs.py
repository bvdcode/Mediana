# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Compare complete decoded samples with bounded validation scratch memory."""

from __future__ import annotations

import argparse
import json
import math
import wave
from collections.abc import Callable
from pathlib import Path

import numpy as np


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


def validate_pair(left: Path, right: Path) -> dict[str, int | float | str]:
    """Compare raw RGBA bytes or PCM16 WAVE data, excluding container metadata."""
    if left.suffix == ".rgba":
        with left.open("rb") as first, right.open("rb") as second:
            return {
                "representation": "source-color RGBA8",
                **compare_samples(first.read, second.read, "u1"),
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
    args = parser.parse_args()
    print(json.dumps(validate_pair(args.left, args.right)))


if __name__ == "__main__":
    main()
