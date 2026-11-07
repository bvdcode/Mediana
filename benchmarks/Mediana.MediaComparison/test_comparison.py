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
from collections.abc import Callable
from pathlib import Path

from comparison_models import output_fingerprint
from frame_validation import compare_frames
from PIL import Image, ImageCms
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

    def test_hls_fingerprint_includes_segment_contents(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            playlist, segment = (
                Path(directory) / "index.m3u8",
                Path(directory) / "segment.ts",
            )
            playlist.write_text("#EXTM3U\nsegment.ts\n#EXT-X-ENDLIST\n")
            segment.write_bytes(b"first")
            size, digest = output_fingerprint(playlist)
            self.assertEqual(size, playlist.stat().st_size + 5)
            segment.write_bytes(b"other")
            self.assertNotEqual(output_fingerprint(playlist)[1], digest)

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

    def test_image_profiles_are_normalized_before_color_comparison(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            left, right = Path(directory) / "left.png", Path(directory) / "right.png"
            image = Image.new("RGBA", (8, 8), (17, 81, 197, 93))
            serialize_profile: Callable[[], bytes] = ImageCms.ImageCmsProfile(
                ImageCms.createProfile("sRGB")
            ).tobytes
            profile = serialize_profile()
            image.save(left, icc_profile=profile)
            image.save(right)
            result = validate_pair(left, right)
            self.assertEqual(result["different_samples"], 0)
            self.assertEqual(result["samples"], 256)

    def test_frame_validation_requires_complete_streams(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            left, right = (
                Path(directory) / "left.framehash",
                Path(directory) / "right.framehash",
            )
            digest = "a" * 64
            left.write_text(f"#tb 0: 1/24\n0, 0, 0, 1, 1536, {digest}\n")
            right.write_text(f"#tb 0: 1/48\n0, 0, 0, 2, 1536, {digest}\n")
            self.assertEqual(compare_frames(left, right)["different_frame_timings"], 0)
            with right.open("a") as stream:
                stream.write(f"0, 2, 2, 2, 1536, {digest}\n")
            with self.assertRaisesRegex(ValueError, "frame counts differ"):
                compare_frames(left, right)


if __name__ == "__main__":
    unittest.main()
