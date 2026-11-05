# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Build the format catalog tables from their CSV sources."""

from __future__ import annotations

import argparse
import csv
import logging
import re
from collections import Counter
from dataclasses import dataclass
from pathlib import Path


CATALOG_ROOT = Path(__file__).resolve().parents[1] / "docs" / "formats"
OPERATIONS = {"read": "R", "decode": "D", "render": "V", "encode": "E", "write": "W"}
STATES = {"planned": "□", "partial": "◐", "complete": "✓"}
CHECK_FIELDS = ("implementation", "samples", "correctness", "cpu", "memory", "aot")


@dataclass(frozen=True)
class Format:
    """Identify one independently testable format variant."""

    id: str
    section: str
    name: str
    variant: str
    kind: str
    extensions: str
    tier: str
    specs: str
    notes: str


@dataclass(frozen=True)
class Check:
    """Track implementation and acceptance for one format operation."""

    format_id: str
    operation: str
    implementation: str
    samples: str
    correctness: str
    cpu: str
    memory: str
    aot: str
    evidence: str


def load_formats(path: Path) -> list[Format]:
    """Read format definitions without changing their order."""
    with path.open(encoding="utf-8", newline="") as stream:
        return [Format(**row) for row in csv.DictReader(stream)]


def load_checks(path: Path) -> list[Check]:
    """Read the operation checklist."""
    with path.open(encoding="utf-8", newline="") as stream:
        return [Check(**row) for row in csv.DictReader(stream)]


def validate(formats: list[Format], checks: list[Check]) -> None:
    """Reject duplicate IDs, unknown references and unsupported states."""
    identifiers = [item.id for item in formats]
    if len(identifiers) != len(set(identifiers)):
        raise ValueError("Duplicate format ID")
    pairs = [(item.format_id, item.operation) for item in checks]
    if len(pairs) != len(set(pairs)):
        raise ValueError("Duplicate format operation")
    spec_ids = set(
        re.findall(
            r'^<a id="([a-z0-9-]+)"></a>$',
            (CATALOG_ROOT / "sources.md").read_text(encoding="utf-8"),
            re.M,
        )
    )
    for item in formats:
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", item.id):
            raise ValueError(f"Invalid format ID: {item.id}")
        if item.tier not in {"A", "B", "C"}:
            raise ValueError(f"Invalid tier: {item.id}")
        if not all((item.section, item.name, item.variant, item.kind, item.specs)):
            raise ValueError(f"Missing format field: {item.id}")
        if set(item.specs.split(";")) - spec_ids:
            raise ValueError(f"Unknown source: {item.id}")
        if not any(check.format_id == item.id for check in checks):
            raise ValueError(f"Missing operations: {item.id}")
    for item in checks:
        if item.format_id not in identifiers or item.operation not in OPERATIONS:
            raise ValueError(f"Unknown format or operation: {item}")
        for field in CHECK_FIELDS:
            if getattr(item, field) not in STATES:
                raise ValueError(f"Invalid {field}: {item}")
        has_progress = any(getattr(item, field) != "planned" for field in CHECK_FIELDS)
        if has_progress and not item.evidence:
            raise ValueError(f"Missing evidence: {item}")
        for reference in filter(None, item.evidence.split(";")):
            if not (CATALOG_ROOT / reference).is_file():
                raise ValueError(f"Missing evidence file: {reference}")


def cell(value: str) -> str:
    """Escape Markdown table delimiters."""
    return value.replace("|", "\\|").replace("\n", " ")


def section_anchor(section: str) -> str:
    """Build an anchor that is independent of section ordering."""
    return re.sub(r"[^a-z0-9]+", "-", section.lower()).strip("-")


def render(formats: list[Format], checks: list[Check]) -> str:
    """Render the overview and grouped format tables."""
    sections = list(dict.fromkeys(item.section for item in formats))
    lines = [
        "# Format implementation catalog",
        "",
        "Mediana's target formats and independently tracked operations. "
        "This is a development map,",
        "not a claim that every listed format is currently supported. "
        "Priorities and scope may evolve.",
        "",
        f"**{len(formats)} variants · {len(checks)} operations · "
        f"{len(sections)} groups.**",
        "",
        "[How to use the checklist and verify completion](guide.md) · "
        "[Specifications](sources.md) ·",
        "[Format definitions (CSV)](catalog.csv) · "
        "[Operation checklist (CSV)](checks.csv)",
        "",
        "R = read structure/demux; D = decode; V = render; "
        "E = encode; W = write/mux/export.",
        "□ planned; ◐ implemented subset; ✓ complete for the stated scope. "
        "A = core; B = expansion;",
        "C = specialist/legacy exploration. "
        "These tiers do not prescribe a fixed development order.",
        "",
        "The tables show implementation only. Samples, correctness, CPU, memory "
        "and Native AOT",
        "have separate cells **for each operation** in `checks.csv`. "
        "An implementation mark",
        "does not close these checks. "
        "Existing subsets remain ◐ until the row's scope is covered.",
        "",
        "Update the two CSV sources, then run `python3 tools/format-catalog.py` "
        "from the repository root.",
        "Verify the tables with `python3 tools/format-catalog.py --check`.", "",
        "| Group | Variants | Operations |", "| --- | ---: | ---: |",
    ]
    for section in sections:
        ids = {item.id for item in formats if item.section == section}
        count = sum(item.format_id in ids for item in checks)
        anchor = section_anchor(section)
        lines.append(f"| [{cell(section)}](#{anchor}) | {len(ids)} | {count} |")
    for section in sections:
        lines.extend(
            [
                "", f"## {section}", "",
                "| ID | Format / variant | Extensions | Operations | Tier | "
                "Scope / sources |",
                "| --- | --- | --- | --- | :---: | --- |",
            ]
        )
        for item in (row for row in formats if row.section == section):
            operations = " · ".join(
                f"{OPERATIONS[check.operation]} {STATES[check.implementation]}"
                for check in checks if check.format_id == item.id
            )
            sources = ", ".join(
                f"[{source}](sources.md#{source})" for source in item.specs.split(";")
            )
            values = [
                f"`{item.id}`", f"{item.name} — {item.variant}", item.extensions or "—",
                operations, item.tier, f"{item.notes} {sources}".strip(),
            ]
            lines.append("| " + " | ".join(cell(value) for value in values) + " |")
    return "\n".join(lines) + "\n"


def main() -> None:
    """Generate the tables or check that the existing tables match."""
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check", action="store_true", help="Fail if the tables need regeneration"
    )
    args = parser.parse_args()
    formats = load_formats(CATALOG_ROOT / "catalog.csv")
    checks = load_checks(CATALOG_ROOT / "checks.csv")
    validate(formats, checks)
    result = render(formats, checks)
    destination = CATALOG_ROOT / "README.md"
    if args.check:
        if not destination.is_file():
            raise SystemExit("Format tables are missing")
        if destination.read_text(encoding="utf-8") != result:
            raise SystemExit("Regenerate tables with python3 tools/format-catalog.py")
    else:
        destination.write_text(result, encoding="utf-8")
    counts = Counter(item.implementation for item in checks)
    logging.info(
        "%d variants; %d operations; %s", len(formats), len(checks), dict(counts)
    )


if __name__ == "__main__":
    main()
