# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Run alternating fresh-process comparisons on Linux without concurrent cases."""

from __future__ import annotations

import argparse
import json
import logging
import os
import platform
import signal
import subprocess
import tempfile
import threading
import time
from dataclasses import asdict
from pathlib import Path

from comparison_models import (
    Command,
    MeasuredOutput,
    Measurement,
    Workload,
    file_hash,
    load_workloads,
    output_fingerprint,
)
from pair_validator import PairValidator

LOGGER = logging.getLogger(__name__)


def measure(
    arguments: list[str], environment: dict[str, str], log: Path, timeout: float
) -> tuple[float, float, int, int, bool]:
    """Wait for this child only and retain its CPU and resident high-water mark."""
    expired = threading.Event()
    started = time.perf_counter()
    with log.open("wb") as output, subprocess.Popen(
        arguments,
        env=environment,
        stdout=output,
        stderr=output,
        start_new_session=True,
    ) as process:

        def terminate() -> None:
            expired.set()
            LOGGER.error("Operation exceeded %.1f seconds", timeout)
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                LOGGER.info("Process exited at the timeout boundary")

        timer = threading.Timer(timeout, terminate)
        timer.start()
        try:
            _, status, usage = os.wait4(process.pid, 0)
            process.returncode = os.waitstatus_to_exitcode(status)
        finally:
            timer.cancel()
            timer.join()
        elapsed = time.perf_counter() - started
        return (
            elapsed,
            usage.ru_utime + usage.ru_stime,
            usage.ru_maxrss * 1024,
            process.returncode,
            expired.is_set(),
        )


def run_case(
    case: Workload,
    command: Command,
    engine: str,
    repetition: int,
    variables: dict[str, str],
    output: Path,
    logs: Path,
    cpus: str,
    timeout: float,
) -> MeasuredOutput:
    """Expand one manifest command, measure it and fingerprint its output."""
    prefix = f"{case.id}-{engine}-{repetition}"
    destination = output / (prefix + case.output_extension)
    command_directory = output / engine
    command_directory.mkdir(exist_ok=True)
    replacements = variables | {
        "input": str(Path(variables["inputs"]) / case.input),
        "output": str(destination),
        "output_stem": str(destination.with_suffix("")),
        "output_directory": str(command_directory),
        "profile_uri": (command_directory / "profile").as_uri(),
        "input_stem": Path(case.input).stem,
    }
    executable = replacements.get(command.tool, command.tool)
    arguments = ["taskset", "-c", cpus, executable] + [
        arg.format_map(replacements) for arg in command.arguments
    ]
    environment = dict(os.environ)
    environment.update(
        DOTNET_PROCESSOR_COUNT=str(len(os.sched_getaffinity(0))),
        DOTNET_TieredCompilation="0",
    )
    environment.update(
        {
            key: value.format_map(replacements)
            for key, value in command.environment.items()
        }
    )
    elapsed, cpu, rss, code, expired = measure(
        arguments, environment, logs / (prefix + ".log"), timeout
    )
    produced = Path(command.output.format_map(replacements))
    if code == 0 and not produced.is_file():
        raise FileNotFoundError("Successful command did not produce its output")
    size, checksum = (0, "")
    if code == 0:
        size, checksum = output_fingerprint(produced)
    return MeasuredOutput(
        Measurement(
            case.id,
            engine,
            repetition,
            elapsed,
            cpu,
            rss,
            code,
            expired,
            size,
            checksum,
        ),
        produced,
    )


def main() -> None:
    """Preserve individual measurements, provenance and failed operations."""
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--inputs", type=Path, required=True)
    parser.add_argument("--mediana", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--scratch",
        type=Path,
        required=True,
        help="Temporary decoded files; one pair is retained at a time",
    )
    parser.add_argument("--revision", required=True)
    parser.add_argument("--cpus", default="2,4")
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--timeout", type=float, default=900)
    parser.add_argument("--case", action="append", default=[])
    parser.add_argument("--variable", action="append", default=[])
    args = parser.parse_args()
    if platform.system() != "Linux":
        parser.error("This runner uses Linux wait4 RSS units and CPU affinity")
    if args.repeats < 1 or args.timeout <= 0:
        parser.error("Repetitions and timeout must be positive")
    cpus = {int(item) for item in args.cpus.split(",")}
    if not cpus or not cpus <= os.sched_getaffinity(0):
        parser.error("Select available explicit logical CPU numbers")
    os.sched_setaffinity(0, cpus)
    workloads = load_workloads(args.manifest)
    selected = set(args.case)
    if selected - {item.id for item in workloads}:
        parser.error("Unknown case ID")
    if selected:
        workloads = [item for item in workloads if item.id in selected]
    variables = {
        "mediana": str(args.mediana.resolve()),
        "inputs": str(args.inputs.resolve()),
    }
    for item in args.variable:
        key, value = item.split("=", 1)
        variables[key] = value
    args.output.mkdir(parents=True, exist_ok=False)
    metadata = {
        "revision": args.revision,
        "mediana_sha256": file_hash(args.mediana),
        "system": platform.platform(),
        "machine": platform.machine(),
        "cpus": sorted(cpus),
        "repeats": args.repeats,
        "scope": "fresh process, complete command, warm filesystem cache",
        "output_storage": "temporary decoded pair, validation and removal outside timing",
        "manifest": json.loads(args.manifest.read_text(encoding="utf-8")),
        "inputs": [
            {
                "case": item.id,
                "filename": item.input,
                "bytes": (args.inputs / item.input).stat().st_size,
                "sha256": file_hash(args.inputs / item.input),
            }
            for item in workloads
        ],
    }
    (args.output / "metadata.json").write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf-8"
    )
    failed = False
    validator = PairValidator(args.output, args.timeout)
    with (args.output / "measurements.jsonl").open("w", encoding="utf-8") as stream, (
        args.output / "validation.jsonl"
    ).open("w", encoding="utf-8") as validation:
        for repetition in range(1, args.repeats + 1):
            for index, case in enumerate(workloads):
                engines = [("mediana", case.mediana), ("alternative", case.alternative)]
                if (repetition + index) % 2 == 0:
                    engines.reverse()
                with tempfile.TemporaryDirectory(
                    dir=args.scratch, prefix="media-pair-"
                ) as directory:
                    pair = Path(directory)
                    succeeded = True
                    products: dict[str, MeasuredOutput] = {}
                    for engine, command in engines:
                        LOGGER.info("%s %s repetition %d", case.id, engine, repetition)
                        result = run_case(
                            case,
                            command,
                            engine,
                            repetition,
                            variables,
                            pair,
                            args.output.resolve(),
                            args.cpus,
                            args.timeout,
                        )
                        products[engine] = result
                        stream.write(json.dumps(asdict(result.measurement)) + "\n")
                        stream.flush()
                        succeeded &= result.measurement.exit_code == 0
                    failed |= not succeeded
                    if succeeded:
                        samples = validator.compare(
                            case.id, repetition, args.inputs / case.input, products
                        )
                        failed |= not bool(samples["validation_succeeded"])
                        validation.write(
                            json.dumps(
                                {"case": case.id, "repetition": repetition, **samples}
                            )
                            + "\n"
                        )
                        validation.flush()
    if failed:
        raise SystemExit("Some operations failed; inspect their measurements and logs")


if __name__ == "__main__":
    main()
