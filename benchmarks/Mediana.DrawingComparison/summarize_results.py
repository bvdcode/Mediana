# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Aggregate every paired workload, retaining repetitions and memory tradeoffs."""

from __future__ import annotations

import argparse
import json
import logging
import statistics
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import cast


@dataclass(frozen=True)
class EngineSummary:
    """Medians and independent-process ranges for one engine and workload."""

    repetitions: int
    operations: int
    elapsed_ms: float
    elapsed_min_ms: float
    elapsed_max_ms: float
    cpu_ms: float
    allocated_bytes: float
    peak_rss_bytes: float
    largest_peak_rss_bytes: float
    cold_ms: float
    font_load_ms: float
    observed_parallelism: int


def numeric(measurement: dict[str, object], key: str) -> float:
    """Reject absent or nonnumeric measurement fields."""
    value = measurement[key]
    if not isinstance(value, (int, float)):
        raise ValueError(f"Expected a number for {key}.")
    return float(value)


def summarize(rows: list[dict[str, object]]) -> EngineSummary:
    """Keep medians separate from worst observed process memory."""
    elapsed = [numeric(row, "ElapsedMillisecondsPerImage") for row in rows]
    peaks = [numeric(row, "PeakWorkingSetBytes") for row in rows]
    return EngineSummary(
        len(rows),
        sum(int(numeric(row, "Operations")) for row in rows),
        statistics.median(elapsed),
        min(elapsed),
        max(elapsed),
        statistics.median(numeric(row, "CpuMillisecondsPerImage") for row in rows),
        statistics.median(
            numeric(row, "ManagedAllocatedBytesPerImage") for row in rows
        ),
        statistics.median(peaks),
        max(peaks),
        statistics.median(numeric(row, "ColdMilliseconds") for row in rows),
        statistics.median(numeric(row, "FontLoadMilliseconds") for row in rows),
        min(int(numeric(row, "ObservedParallelism")) for row in rows),
    )


def main() -> None:
    """Write complete JSON and Markdown tables, including missed speed targets."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", type=Path, nargs="+")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    grouped: dict[str, dict[str, list[dict[str, object]]]] = {}
    for path in args.inputs:
        for line in path.read_text(encoding="utf-8").splitlines():
            record = json.loads(line)
            case = f"{path.stem}/{record['case']}"
            row = cast(dict[str, object], record["measurement"])
            engine = str(row["Engine"])
            grouped.setdefault(case, {}).setdefault(engine, []).append(row)
    results: list[dict[str, object]] = []
    table = [
        "| Workload | ms Mediana / reference | CPU ratio | Allocated MiB M / R | Peak RSS MiB M / R | Time ratio |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for case, engines in grouped.items():
        mediana = summarize(engines["mediana"])
        reference = summarize(engines["imagesharp"])
        if mediana.repetitions != reference.repetitions:
            raise ValueError(f"Incomplete paired repetitions for {case}.")
        time_ratio = mediana.elapsed_ms / reference.elapsed_ms
        cpu_ratio = mediana.cpu_ms / reference.cpu_ms
        results.append(
            {
                "case": case,
                "mediana": asdict(mediana),
                "reference": asdict(reference),
                "time_ratio": time_ratio,
                "cpu_ratio": cpu_ratio,
                "within_5_percent_time": time_ratio <= 1.05,
            }
        )
        mib = 1024 * 1024
        table.append(
            f"| {case} | {mediana.elapsed_ms:.3f} / {reference.elapsed_ms:.3f} | "
            f"{cpu_ratio:.3f} | {mediana.allocated_bytes / mib:.3f} / "
            f"{reference.allocated_bytes / mib:.3f} | "
            f"{mediana.peak_rss_bytes / mib:.1f} / "
            f"{reference.peak_rss_bytes / mib:.1f} | {time_ratio:.3f} |"
        )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.with_suffix(".json").write_text(
        json.dumps(results, indent=2) + "\n", encoding="utf-8"
    )
    args.output.with_suffix(".md").write_text("\n".join(table) + "\n", encoding="utf-8")
    passed = sum(result["within_5_percent_time"] is True for result in results)
    logging.info("Speed target reached in %d/%d workloads.", passed, len(results))


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    main()
