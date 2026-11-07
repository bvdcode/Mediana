# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Validate whole AVC/AAC transport outputs without storing decoded media."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import tempfile
from pathlib import Path


def probe(path: Path, selector: str) -> list[dict[str, str | int]]:
    """Read explicit codec, geometry and parsed-packet counters from ffprobe."""
    process = subprocess.run(
        [
            "ffprobe",
            "-v",
            "error",
            "-select_streams",
            selector,
            "-count_packets",
            "-show_entries",
            "stream=codec_name,width,height,nb_read_packets",
            "-of",
            "json",
            str(path),
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    value = json.loads(process.stdout)
    streams: list[dict[str, str | int]] = value["streams"]
    return streams


def video_quality(
    source: Path, candidate: Path, stats: Path, expected: int
) -> tuple[str, int]:
    """Decode every frame and compare by presentation order with FFmpeg PSNR."""
    graph = (
        "[0:v:0]settb=1/1,setpts=N[reference];"
        "[1:v:0]settb=1/1,setpts=N[candidate];"
        f"[reference][candidate]psnr=stats_file={stats}:shortest=1"
    )
    process = subprocess.run(
        [
            "ffmpeg",
            "-nostdin",
            "-nostats",
            "-v",
            "info",
            "-xerror",
            "-threads",
            "2",
            "-i",
            str(source),
            "-threads",
            "2",
            "-i",
            str(candidate),
            "-filter_complex_threads",
            "2",
            "-filter_complex",
            graph,
            "-an",
            "-fps_mode",
            "passthrough",
            "-threads",
            "2",
            "-f",
            "null",
            "-",
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    frames = differences = 0
    with stats.open(encoding="utf-8") as stream:
        for line in stream:
            values = dict(part.split(":", 1) for part in line.split())
            differences += values["psnr_avg"] != "inf"
            frames += 1
    if frames != expected:
        raise ValueError(
            f"Incomplete quality comparison: {frames} of {expected} frames"
        )
    match = re.search(r"PSNR .* average:([0-9.inf]+)", process.stderr)
    if match is None:
        raise ValueError("Missing complete-stream PSNR summary")
    return match[1], differences


def audio_hash(path: Path) -> str:
    """Fingerprint every copied AAC access unit with canonical ADTS headers."""
    with tempfile.TemporaryFile() as errors, subprocess.Popen(
        [
            "ffmpeg",
            "-nostdin",
            "-v",
            "error",
            "-xerror",
            "-i",
            str(path),
            "-map",
            "0:a:0",
            "-c:a",
            "copy",
            "-f",
            "adts",
            "-",
        ],
        stdout=subprocess.PIPE,
        stderr=errors,
    ) as process:
        if process.stdout is None:
            raise RuntimeError("Missing access-unit stream")
        digest = hashlib.sha256()
        while chunk := process.stdout.read(1024 * 1024):
            digest.update(chunk)
        if process.wait() != 0:
            errors.seek(0)
            raise RuntimeError(errors.read().decode("utf-8"))
        return digest.hexdigest()


def compare_media(
    left: Path, right: Path, source: Path
) -> dict[str, int | float | str]:
    """Check frame completeness, source quality and preservation of copied audio."""
    original = probe(source, "v:0")
    if len(original) != 1:
        raise ValueError("Expected exactly one selected source video stream")
    expected = int(original[0]["nb_read_packets"])
    width, height = int(original[0]["width"]), int(original[0]["height"])
    metrics: dict[str, int | float | str] = {
        "frames": expected,
        "width": width,
        "height": height,
        "representation": "full AVC decode; source PSNR by frame order; copied AAC access units",
    }
    with tempfile.TemporaryDirectory() as directory:
        for engine, candidate in (("mediana", left), ("alternative", right)):
            video = probe(candidate, "v:0")
            if len(video) != 1 or int(video[0]["nb_read_packets"]) != expected:
                raise ValueError(
                    "Encoded access-unit counts differ from source pictures"
                )
            if int(video[0]["width"]) != width or int(video[0]["height"]) != height:
                raise ValueError("Encoded dimensions differ from the source")
            quality, differences = video_quality(
                source, candidate, Path(directory) / (engine + ".txt"), expected
            )
            metrics[engine + "_source_psnr_db"] = quality
            metrics[engine + "_frames_with_pixel_differences"] = differences
    left_audio, right_audio = probe(left, "a:0"), probe(right, "a:0")
    if len(left_audio) != len(right_audio):
        raise ValueError("Output audio selections differ")
    if left_audio:
        if (
            len(left_audio) != 1
            or left_audio[0]["codec_name"] != "aac"
            or right_audio[0]["codec_name"] != "aac"
        ):
            raise ValueError("Expected one copied AAC stream")
        original_hash = audio_hash(source)
        metrics["mediana_copied_audio_matches_source"] = int(
            audio_hash(left) == original_hash
        )
        metrics["alternative_copied_audio_matches_source"] = int(
            audio_hash(right) == original_hash
        )
        metrics["source_audio_adts_sha256"] = original_hash
    return metrics
