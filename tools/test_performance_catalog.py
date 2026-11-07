# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Check workload identities and the provenance of published medians."""

from __future__ import annotations

import csv
import json
import statistics
import tempfile
import unittest
from pathlib import Path

from performance_catalog import Comparison, load_comparisons, load_results


class PerformanceCatalogTests(unittest.TestCase):
    """Keep the public table tied to successful, repeatable individual runs."""

    root = Path(__file__).resolve().parents[1] / "docs"

    def test_published_medians_match_individual_measurements(self) -> None:
        with (self.root / "formats/catalog.csv").open(newline="") as stream:
            identifiers = {item["id"] for item in csv.DictReader(stream)}
        cases = load_comparisons(self.root / "performance/cases.csv", identifiers)
        results = load_results(self.root / "performance/results.csv", cases)
        for result in results.values():
            raw = (
                self.root
                / "performance"
                / Path(result.report).parent
                / "measurements.jsonl"
            )
            records = [json.loads(line) for line in raw.read_text().splitlines()]
            for engine in ("alternative", "mediana"):
                selected = [
                    row
                    for row in records
                    if row["case"] == result.case and row["engine"] == engine
                ]
                self.assertGreaterEqual(len(selected), 3)
                self.assertTrue(
                    all(
                        row["exit_code"] == 0 and not row["timed_out"]
                        for row in selected
                    )
                )
                self.assertEqual(len({row["output_sha256"] for row in selected}), 1)
                for metric in ("elapsed_seconds", "cpu_seconds", "peak_rss_bytes"):
                    self.assertEqual(
                        float(getattr(result, f"{engine}_{metric}")),
                        statistics.median(row[metric] for row in selected),
                    )

    def test_unknown_format_reference_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "cases.csv"
            path.write_text(
                "id,format_ids,operation,workload,alternative,track,report\n"
                "photo,unknown,decode,Photo,Reference,ordinary,\n"
            )
            with self.assertRaisesRegex(ValueError, "Unknown comparison format"):
                load_comparisons(path, {"jpeg-baseline"})

    def test_nonfinite_measurement_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "report.md").touch()
            path = root / "results.csv"
            path.write_text(
                "case,alternative_elapsed_seconds,alternative_cpu_seconds,"
                "alternative_peak_rss_bytes,mediana_elapsed_seconds,"
                "mediana_cpu_seconds,mediana_peak_rss_bytes,report,quality\n"
                "photo,inf,1,100,1,1,100,report.md,Exact samples\n"
            )
            case = Comparison(
                "photo", "jpeg-baseline", "decode", "Photo", "Reference", "ordinary", ""
            )
            with self.assertRaisesRegex(ValueError, "Invalid measurement"):
                load_results(path, [case])


if __name__ == "__main__":
    unittest.main()
