# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Exercise process accounting, timeouts and decoded-sample validation."""

from __future__ import annotations

import io
import os
import struct
import subprocess
import sys
import tempfile
import unittest
import wave
from pathlib import Path

from run_comparison import measure
from validate_outputs import compare_samples, validate_pair


class ProcessMeasurementTests(unittest.TestCase):
    """The timed child must not consume another process's exit or usage."""

    def test_waits_for_specific_child_and_preserves_nonzero_exit(self) -> None:
        with tempfile.TemporaryDirectory() as directory, subprocess.Popen(
            [sys.executable, "-c", "raise SystemExit(7)"]
        ) as other:
            result = measure(
                [sys.executable, "-c", "raise SystemExit(3)"],
                dict(os.environ),
                Path(directory) / "run.log",
                5,
            )
            self.assertEqual(result[3:], (3, False))
            self.assertGreater(result[2], 0)
            self.assertEqual(other.wait(), 7)

    def test_timeout_terminates_and_reaps_child(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            result = measure(
                [sys.executable, "-c", "import time; time.sleep(20)"],
                dict(os.environ),
                Path(directory) / "run.log",
                0.1,
            )
            self.assertEqual(result[3:], (-9, True))


class SampleValidationTests(unittest.TestCase):
    """Compare decoded data rather than file-container identity."""

    def test_signed_pcm_subtraction_does_not_overflow(self) -> None:
        result = compare_samples(
            io.BytesIO(struct.pack("<hh", -32768, 32767)).read,
            io.BytesIO(struct.pack("<hh", 32767, -32768)).read,
            "<i2",
        )
        self.assertEqual(result["max_absolute_error"], 65535)
        self.assertEqual(result["different_samples"], 2)

    def test_sample_length_mismatch_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "lengths differ"):
            compare_samples(io.BytesIO(b"12").read, io.BytesIO(b"1").read, "u1")

    def test_wave_metadata_is_excluded_but_all_frames_are_compared(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            left = Path(directory) / "left.wav"
            right = Path(directory) / "right.wav"
            with wave.open(str(left), "wb") as output:
                output.setparams((1, 2, 48000, 0, "NONE", "not compressed"))
                output.writeframes(struct.pack("<hhh", -1, 0, 32767))
            original = left.read_bytes()
            tagged = bytearray(
                original[:36] + b"JUNK\x04\x00\x00\x00test" + original[36:]
            )
            struct.pack_into("<I", tagged, 4, len(tagged) - 8)
            right.write_bytes(tagged)
            result = validate_pair(left, right)
            self.assertEqual(result["max_absolute_error"], 0)
            self.assertEqual(result["frames"], 3)


if __name__ == "__main__":
    unittest.main()
