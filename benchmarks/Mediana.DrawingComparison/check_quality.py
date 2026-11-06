# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Compare native coverage with aligned supersampled output and save image previews."""

from __future__ import annotations

import argparse
import json
import logging
import math
import os
import subprocess
from dataclasses import asdict, dataclass, replace
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter, ImageStat

from run_comparison import Workload, workloads


@dataclass(frozen=True)
class QualityResult:
    """Coverage errors against a common supersampled reference, in byte codes."""

    case: str
    supersampling: int
    mediana_edge_rmse: float
    reference_edge_rmse: float
    direct_edge_rmse: float
    mediana_alpha_rmse: float
    reference_alpha_rmse: float
    passed: bool


def flatten(image: Image.Image) -> Image.Image:
    """Composite straight RGBA on white without losing transparent edge coverage."""
    background = Image.new("RGBA", image.size, "white")
    background.alpha_composite(image)
    return background.convert("RGB")


def rms_difference(
    first: Image.Image, second: Image.Image, mask: Image.Image | None = None
) -> float:
    """Return channel-averaged root mean square error."""
    values = ImageStat.Stat(ImageChops.difference(first, second), mask).rms
    return math.sqrt(sum(value * value for value in values) / len(values))


def render(
    args: argparse.Namespace, case: Workload, engine: str, folder: Path
) -> Image.Image:
    """Export pixels after the child has completed its rendering measurement."""
    environment = dict(
        os.environ, DOTNET_PROCESSOR_COUNT="2", DOTNET_TieredCompilation="0"
    )
    command = [
        str(args.dotnet),
        str(args.binary),
        engine,
        case.scene,
        str(case.width),
        str(case.height),
        str(case.complexity),
        str(args.font_directory / case.font),
        "1",
        "0",
        "1",
        str(folder),
        str(case.scale),
    ]
    subprocess.run(command, env=environment, capture_output=True, text=True, check=True)
    path = folder / f"{engine}.rgba"
    image = Image.frombytes(
        "RGBA", (case.width * case.scale, case.height * case.scale), path.read_bytes()
    )
    path.unlink()
    return image


def check(args: argparse.Namespace, case: Workload) -> QualityResult:
    """Check native images against premultiplied box-filtered area coverage."""
    folder = args.output / case.key
    mediana = render(args, case, "mediana", folder)
    reference = render(args, case, "imagesharp", folder)
    factor = 8 if max(case.width, case.height) <= 200 else 4
    large = render(args, replace(case, scale=case.scale * factor), "imagesharp", folder)
    truth = (
        large.convert("RGBa").resize(mediana.size, Image.Resampling.BOX).convert("RGBA")
    )
    large.close()
    truth_rgb = flatten(truth)
    luminance = truth_rgb.convert("L")
    edge = ImageChops.difference(
        luminance.filter(ImageFilter.MaxFilter(3)),
        luminance.filter(ImageFilter.MinFilter(3)),
    )
    mask = edge.point(lambda value: 255 if value else 0)
    actual_rgb, reference_rgb = flatten(mediana), flatten(reference)
    actual_error = rms_difference(actual_rgb, truth_rgb, mask)
    reference_error = rms_difference(reference_rgb, truth_rgb, mask)
    result = QualityResult(
        case.key,
        factor,
        actual_error,
        reference_error,
        rms_difference(actual_rgb, reference_rgb, mask),
        rms_difference(mediana.getchannel("A"), truth.getchannel("A")),
        rms_difference(reference.getchannel("A"), truth.getchannel("A")),
        actual_error <= reference_error + 1,
    )
    # Full-resolution images allow inspection of small text and fractional strokes.
    actual_rgb.save(folder / "mediana.png")
    reference_rgb.save(folder / "imagesharp.png")
    truth_rgb.save(folder / "supersampled.png")
    preview_width = min(case.width, 640)
    preview_height = round(case.height * preview_width / case.width)
    contact = Image.new("RGB", (preview_width * 3, preview_height), "white")
    for index, image in enumerate([actual_rgb, reference_rgb, truth_rgb]):
        contact.paste(
            image.resize((preview_width, preview_height), Image.Resampling.LANCZOS),
            (index * preview_width, 0),
        )
    contact.save(folder / "comparison.png")
    return result


def quality_cases(suite: str) -> list[Workload]:
    """Cover each requested face and independent vector complexity dimensions."""
    if suite == "fonts":
        return workloads("fonts")
    if suite == "vectors":
        return [case for case in workloads("vectors") if case.width <= 640]
    raise ValueError(f"Unknown suite: {suite}")


def main() -> None:
    """Write all quality outcomes without stopping at the first failed scene."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--font-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--suite", choices=["fonts", "vectors"], required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    results: list[QualityResult] = []
    with (args.output / "quality.jsonl").open("w", encoding="utf-8") as output:
        for case in quality_cases(args.suite):
            result = check(args, case)
            results.append(result)
            output.write(json.dumps(asdict(result)) + "\n")
            output.flush()
            logging.info(
                "%s: %.3f vs %.3f, passed=%s",
                case.key,
                result.mediana_edge_rmse,
                result.reference_edge_rmse,
                result.passed,
            )
    if not all(result.passed for result in results):
        raise SystemExit("One or more scenes did not meet the image-quality threshold.")


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    main()
