# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Validate representative workloads and render their comparison table."""

from __future__ import annotations

import csv
import math
import re
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Comparison:
    """Link format variants to representative whole-operation workloads."""

    id: str
    format_ids: str
    operation: str
    workload: str
    alternative: str
    track: str
    report: str


@dataclass(frozen=True)
class Result:
    """Store medians from one identified pair of measurement series."""

    case: str
    alternative_elapsed_seconds: str
    alternative_cpu_seconds: str
    alternative_peak_rss_bytes: str
    mediana_elapsed_seconds: str
    mediana_cpu_seconds: str
    mediana_peak_rss_bytes: str
    report: str


def load_comparisons(path: Path, identifiers: set[str]) -> list[Comparison]:
    """Reject unknown format links, duplicate cases and missing report files."""
    with path.open(encoding="utf-8", newline="") as stream:
        cases = [Comparison(**row) for row in csv.DictReader(stream)]
    if len(cases) != len({item.id for item in cases}):
        raise ValueError("Duplicate comparison ID")
    for item in cases:
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", item.id):
            raise ValueError(f"Invalid comparison ID: {item.id}")
        if set(item.format_ids.split(";")) - identifiers:
            raise ValueError(f"Unknown comparison format: {item.id}")
        if item.operation not in {"read", "decode", "render", "encode", "write"}:
            raise ValueError(f"Invalid comparison operation: {item.id}")
        if (
            item.track not in {"ordinary", "heavy"}
            or not item.workload
            or not item.alternative
        ):
            raise ValueError(f"Invalid comparison scope: {item.id}")
        if item.report and not (path.parent / item.report).is_file():
            raise ValueError(f"Missing comparison report: {item.id}")
    return cases


def load_results(path: Path, cases: list[Comparison]) -> dict[str, Result]:
    """Check that numerical summaries belong to known cases and linked reports."""
    with path.open(encoding="utf-8", newline="") as stream:
        rows = [Result(**row) for row in csv.DictReader(stream)]
    if len(rows) != len({item.case for item in rows}):
        raise ValueError("Duplicate measured case")
    identifiers = {item.id for item in cases}
    for item in rows:
        if item.case not in identifiers or not (path.parent / item.report).is_file():
            raise ValueError(f"Unknown measured case or missing report: {item.case}")
        for engine in ("alternative", "mediana"):
            for metric in ("elapsed_seconds", "cpu_seconds", "peak_rss_bytes"):
                value = float(getattr(item, f"{engine}_{metric}"))
                if not math.isfinite(value) or value <= 0:
                    raise ValueError(
                        f"Invalid measurement: {item.case}/{engine}/{metric}"
                    )
    return {item.case: item for item in rows}


def escape(value: str) -> str:
    """Escape Markdown table delimiters."""
    return value.replace("|", "\\|").replace("\n", " ")


def metrics(result: Result, engine: str) -> str:
    """Show elapsed time, CPU seconds and peak resident MiB independently."""
    elapsed = float(getattr(result, f"{engine}_elapsed_seconds"))
    cpu = float(getattr(result, f"{engine}_cpu_seconds"))
    memory = float(getattr(result, f"{engine}_peak_rss_bytes")) / (1024 * 1024)
    return f"{elapsed:.3f} s / {cpu:.3f} s / {memory:.1f} MiB"


def render_performance(
    document: str, cases: list[Comparison], results: dict[str, Result]
) -> str:
    """Replace the comparison table while retaining its methodology text."""
    start = "<!-- comparisons:start -->"
    end = "<!-- comparisons:end -->"
    if document.count(start) != 1 or document.count(end) != 1:
        raise ValueError("Performance table markers are missing or duplicated")
    prefix, remainder = document.split(start)
    _, suffix = remainder.split(end)
    lines = [
        (
            "| Case | Operation / representative workload | Track | Alternative | "
            "Alternative: elapsed / CPU / peak RAM | Mediana: elapsed / CPU / peak RAM | Evidence |"
        ),
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for item in cases:
        result = results.get(item.id)
        if result is not None:
            reference = metrics(result, "alternative")
            mediana = metrics(result, "mediana")
            evidence = f"[Measured subset]({result.report})"
        else:
            reference = mediana = "—"
            evidence = (
                f"[Related measurements]({item.report}); target run open"
                if item.report
                else "Target run open"
            )
        values = [
            f'<a id="{item.id}"></a>`{item.id}`',
            f"{item.operation}: {item.workload}",
            item.track,
            item.alternative,
            reference,
            mediana,
            evidence,
        ]
        lines.append("| " + " | ".join(escape(value) for value in values) + " |")
    return prefix + start + "\n\n" + "\n".join(lines) + "\n\n" + end + suffix
