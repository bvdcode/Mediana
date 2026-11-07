# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Validate distinct complete outputs and reuse proof for byte-identical repeats."""

from __future__ import annotations

import json
import logging
import subprocess
import sys
from pathlib import Path

from comparison_models import MeasuredOutput

LOGGER = logging.getLogger(__name__)


class PairValidator:
    """Cache comparisons only within a run with one immutable input per case."""

    def __init__(self, directory: Path, timeout: float) -> None:
        self.directory: Path = directory
        self.timeout: float = timeout
        self._verified: dict[
            tuple[str, str, str], tuple[int, dict[str, int | float | str]]
        ] = {}

    def compare(
        self,
        case: str,
        repetition: int,
        source: Path,
        outputs: dict[str, MeasuredOutput],
    ) -> dict[str, int | float | str]:
        """Fingerprint every output; compute sample metrics once per distinct pair."""
        left, right = outputs["mediana"], outputs["alternative"]
        key = (case, left.measurement.output_sha256, right.measurement.output_sha256)
        cached = self._verified.get(key)
        if cached is not None:
            origin, samples = cached
            return samples | {
                "validation_method": "identical complete output fingerprints",
                "source_validation_repetition": origin,
            }
        checked = subprocess.run(
            [
                sys.executable,
                str(Path(__file__).with_name("validate_outputs.py")),
                str(left.path),
                str(right.path),
                "--source",
                str(source),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=self.timeout,
        )
        if checked.returncode != 0:
            LOGGER.error("%s sample validation failed", case)
            (self.directory / f"{case}-{repetition}-validation.log").write_text(
                checked.stderr, encoding="utf-8"
            )
            return {"validation_succeeded": False, "exit_code": checked.returncode}
        samples = {"validation_succeeded": True, **json.loads(checked.stdout)}
        self._verified[key] = (repetition, samples)
        return samples | {
            "validation_method": "complete output comparison",
            "source_validation_repetition": repetition,
        }
