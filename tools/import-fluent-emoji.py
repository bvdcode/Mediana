#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Import a pinned Fluent Emoji checkout into a reproducible resource archive."""

import argparse
import hashlib
import json
import logging
import subprocess
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Asset:
    """One Unicode sequence and its original SVG resource."""

    sequence: str
    path: Path
    requires_selector: bool


def discover(root: Path) -> list[Asset]:
    """Map metadata and explicit skin-tone variants without filename guessing."""
    tones = ["Default", "Light", "Medium-Light", "Medium", "Medium-Dark", "Dark"]
    assets: list[Asset] = []
    for metadata in sorted((root / "assets").glob("*/metadata.json")):
        document = json.loads(metadata.read_text(encoding="utf-8"))
        sequences: list[str] = document.get("unicodeSkintones", [])
        variants = list(zip(sequences, tones, strict=True)) if sequences else [(document["unicode"], "")]
        for sequence, tone in variants:
            files = list((metadata.parent / tone / "Flat").glob("*.svg"))
            if len(files) != 1:
                raise ValueError(f"Expected one Flat SVG: {metadata.parent.name}/{tone}")
            points = [int(value, 16) for value in sequence.split()]
            normalized = [point for point in points if point != 0xFE0F]
            key = "-".join(f"{point:x}" for point in normalized)
            assets.append(Asset(key, files[0], len(normalized) == 1 and 0xFE0F in points))
    if len({asset.sequence for asset in assets}) != len(assets):
        raise ValueError("Duplicate Unicode sequence")
    if {asset.path for asset in assets} != set((root / "assets").rglob("Flat/*.svg")):
        raise ValueError("Flat SVG coverage does not match metadata")
    return sorted(assets, key=lambda asset: asset.sequence)


def add(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    """Write stable timestamps, permissions and compression settings."""
    entry = zipfile.ZipInfo(name, date_time=(2020, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_DEFLATED
    entry.external_attr = 0o100644 << 16
    archive.writestr(entry, data, compresslevel=9)


def import_assets(root: Path, output: Path, include_skin_tones: bool, dotnet: str) -> None:
    """Preserve original SVG bytes, hashes and commit identity."""
    commit = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    assets = discover(root)
    if not include_skin_tones:
        assets = [asset for asset in assets if not any(0x1F3FB <= int(point, 16) <= 0x1F3FF for point in asset.sequence.split("-"))]
    output.mkdir(parents=True, exist_ok=True)
    manifest: list[dict[str, str]] = []
    with tempfile.TemporaryDirectory(prefix="fluent-emoji-") as directory:
        raw_archive = Path(directory) / "svg.zip"
        write_archive(root, raw_archive, assets, manifest)
        subprocess.run([dotnet, "run", "--file", str(Path(__file__).with_name("compress-fluent-emoji.cs")),
                        "--", str(raw_archive), str(output / "FluentEmoji.zip")], check=True)
    provenance = {"repository": "https://github.com/microsoft/fluentui-emoji", "commit": commit,
                  "style": "Flat", "license": "MIT", "assets": len(assets), "skinToneVariants": include_skin_tones,
                  "compression": "Brotli, independent blocks up to 128 KiB",
                  "archiveSha256": hashlib.sha256((output / "FluentEmoji.zip").read_bytes()).hexdigest()}
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    logging.info("Imported %d SVG resources (%d archive bytes)", len(assets), (output / "FluentEmoji.zip").stat().st_size)


def write_archive(root: Path, path: Path, assets: list[Asset], manifest: list[dict[str, str]]) -> None:
    """Write validated inputs for the BCL block compressor."""
    with zipfile.ZipFile(path, "w") as archive:
        rows: list[str] = []
        for asset in assets:
            data = asset.path.read_bytes()
            add(archive, f"{asset.sequence}.svg", data)
            rows.append(f"{asset.sequence}\t{int(asset.requires_selector)}")
            manifest.append({"sequence": asset.sequence, "source": asset.path.relative_to(root).as_posix(),
                             "sha256": hashlib.sha256(data).hexdigest()})
        add(archive, "index.tsv", ("\n".join(rows) + "\n").encode())
        add(archive, "manifest.json", (json.dumps(manifest, indent=2) + "\n").encode())


def main() -> None:
    """Read source and destination paths from the command line."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkout", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--include-skin-tones", action="store_true", help="Include additional skin-tone SVG variants")
    parser.add_argument("--dotnet", default="dotnet", help=".NET 10 SDK executable for BCL Brotli compression")
    arguments = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    import_assets(arguments.checkout.resolve(), arguments.output.resolve(), arguments.include_skin_tones, arguments.dotnet)


if __name__ == "__main__":
    main()
