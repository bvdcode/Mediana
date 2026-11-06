# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Run paired drawing processes with common CPU allowances and alternating order."""

from __future__ import annotations

import argparse
import json
import logging
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

from comparison_metadata import metadata


@dataclass(frozen=True)
class Workload:
    """One scene, font, canvas, and concurrency configuration."""

    scene: str
    width: int
    height: int
    complexity: int = 1
    font: str = "Tuffy.ttf"
    scale: int = 1
    workers: int = 1
    processors: int = 1

    @property
    def key(self) -> str:
        """Return a portable workload identifier."""
        return (
            f"{self.scene}-{self.width}x{self.height}-c{self.complexity}"
            f"-{self.font}-s{self.scale}-p{self.workers}-cpu{self.processors}"
        )


def workloads(suite: str) -> list[Workload]:
    """Expand independent scene dimensions without engine-specific cases."""
    text = ["short-text", "dense-text", "mixed-text", "table-empty", "table-full"]
    vectors = [
        "bars",
        "curves",
        "strokes",
        "alpha",
        "clipped-curves",
        "transformed-curves",
    ]
    if suite == "calibration":
        return [
            Workload(scene, size, size)
            for scene in text + vectors
            for size in [200, 2560]
        ]
    if suite == "fonts":
        return [
            Workload(scene, width, height, font=font)
            for font in ["Y224.ttf", "Tuffy.ttf", "TuffyBold.ttf", "TuffyItalic.ttf"]
            for width, height in [(200, 200), (640, 480), (2560, 2560)]
            for scene in text
        ]
    if suite == "vectors":
        return [
            Workload(scene, width, height, complexity)
            for scene in vectors
            for width, height in [(200, 200), (640, 480), (2560, 2560)]
            for complexity in [1, 6]
        ]
    if suite == "series":
        return [
            Workload(
                scene, width, height, complexity, workers=workers, processors=workers
            )
            for scene, width, height, complexity in [
                ("dense-text", 2560, 2560, 1),
                ("table-full", 640, 480, 1),
                ("bars", 2560, 2560, 1),
                ("strokes", 640, 480, 6),
            ]
            for workers in [1, 2, 4]
        ]
    if suite == "large":
        return [
            Workload(scene, 2560, 2560, scale=4)
            for scene in ["dense-text", "mixed-text", "bars"]
        ]
    raise ValueError(f"Unknown suite: {suite}")


def main() -> None:
    """Write raw measurements as JSON lines and preserve paired ordering."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--font-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--suite",
        choices=["calibration", "fonts", "vectors", "series", "large"],
        required=True,
    )
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--seconds", type=float, default=3)
    parser.add_argument("--iterations", type=int, default=5)
    args = parser.parse_args()
    if args.repeats < 1 or args.seconds <= 0 or args.iterations < 1:
        parser.error("Repeats, seconds, and iterations must be positive.")
    if args.output.exists():
        raise FileExistsError(
            "Choose a new output file to preserve previous measurements."
        )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    description = metadata(args.dotnet, args.binary, args.suite)
    description.update(
        repeats=args.repeats,
        minimum_seconds=args.seconds,
        minimum_iterations=args.iterations,
    )
    args.output.with_suffix(".metadata.json").write_text(
        json.dumps(description, indent=2) + "\n", encoding="utf-8"
    )
    cases = workloads(args.suite)
    with args.output.open("w", encoding="utf-8") as output:
        for repetition in range(args.repeats):
            for index, case in enumerate(cases):
                engines = ["mediana", "imagesharp"]
                if (repetition + index) % 2:
                    engines.reverse()
                for engine in engines:
                    environment = dict(os.environ)
                    environment.update(
                        DOTNET_PROCESSOR_COUNT=str(case.processors),
                        DOTNET_TieredCompilation="0",
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
                        str(args.iterations),
                        str(args.seconds),
                        str(case.workers),
                        "-",
                        str(case.scale),
                    ]
                    process = subprocess.run(
                        command,
                        env=environment,
                        capture_output=True,
                        text=True,
                        check=True,
                    )
                    measurement: dict[str, object] = json.loads(process.stdout)
                    output.write(
                        json.dumps(
                            {
                                "case": case.key,
                                "repeat": repetition,
                                "measurement": measurement,
                            }
                        )
                        + "\n"
                    )
                    output.flush()
                    logging.info(
                        "%s %d/%d repeat %d %s: %s ms, %s allocated bytes",
                        engine,
                        index + 1,
                        len(cases),
                        repetition + 1,
                        case.key,
                        measurement["ElapsedMillisecondsPerImage"],
                        measurement["ManagedAllocatedBytesPerImage"],
                    )


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    main()
