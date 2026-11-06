# Whole-operation media comparisons

This Linux runner compares complete files in fresh processes, alternates execution
order, records elapsed time, process CPU time and peak RSS, and validates every
decoded sample outside the measured interval. See the
[workload registry and measurements](../../docs/performance/README.md).

The runner uses Python 3.12+, NumPy for the separate validation process, `taskset`,
FFmpeg, and optional libheif **1.23.2** development headers/library. None of these
reference tools are dependencies of the Mediana packages or CLI.

## Build

Build both managed executables from the same recorded product revision. The HEIF
consumer decodes the primary image into source-color RGBA8; it does not invoke
PNG encoding or an ICC transform. Both HEIF consumers include loading, decoding,
orientation, alpha, writing all visible pixels and process termination.

```sh
dotnet publish tools/Mediana.Cli -c Release -r linux-x64 -p:PublishAot=true -o artifacts/comparison/cli
dotnet publish benchmarks/Mediana.MediaComparison/HeifDecode -c Release -r linux-x64 -o artifacts/comparison/heif
pkg-config --atleast-version=1.23.2 libheif
cc -std=c11 -O2 -Wall -Wextra -Werror benchmarks/Mediana.MediaComparison/heif_reference.c \
  $(pkg-config --cflags --libs libheif) -o artifacts/comparison/heif-reference
```

The native reference explicitly selects libde265, strict decoding, two tile and
codec threads, source color-profile passthrough and nearest-neighbor chroma
upsampling, matching Mediana's existing chroma expansion. Different RGB values
are recorded by maximum error and RMSE, never silently accepted as byte-identical.
API definitions are in the
[libheif 1.23.2 source](https://github.com/strukturag/libheif/tree/v1.23.2/libheif/api/libheif).

## Run

Install validation dependencies with
`python3 -m pip install -r benchmarks/Mediana.MediaComparison/requirements.txt`.

Use the filenames and input hashes in the measurement report. `--case` is
repeatable. Select two available logical CPUs on different physical cores. The
scratch directory must have space for the largest decoded pair, approximately
1.6 GB for the current HEIC case. A pair is validated and deleted before the next
case; repetitions do not accumulate decoded files. Logs and JSON remain in a new
output directory. Use the same output filesystem for both engines and report its
type. Do not use a memory filesystem when measuring storage throughput.

```sh
python3 benchmarks/Mediana.MediaComparison/run_comparison.py \
  --manifest benchmarks/Mediana.MediaComparison/workloads.json \
  --inputs <input-root> --scratch <scratch-root> \
  --mediana artifacts/comparison/cli/Mediana.Cli \
  --variable mediana_heif=<absolute-heif-consumer> \
  --variable heif_reference=<absolute-native-reference> \
  --variable heif_libraries=<libheif-library-directory> \
  --revision <product-commit> --cpus 2,4 --repeats 3 \
  --output artifacts/comparison/run
python3 -m unittest discover -s benchmarks/Mediana.MediaComparison -p 'test_*.py'
```

`metadata.json` preserves input and CLI hashes, the command templates, affinity,
revision and repetition count. `measurements.jsonl` preserves each process result,
including failures/timeouts. `validation.jsonl` records decoded sample counts,
maximum absolute error and RMSE. File hashes include headers; PCM validation
excludes WAVE metadata. Numerical validation runs in a separate process, keeping
its imports and buffers out of subsequent measured children.

CPU is Linux `wait4` user + system time for the specific child; peak RSS is its
resident high-water mark, converted from KiB to bytes. It includes runtime/native
process memory and is not a managed-allocation counter or peak system RAM.
Elapsed time includes startup, I/O and cleanup. File hashing and validation happen
after timing. A CPU allowance does not establish equal internal parallelism.

These workloads have no child-process execution inside the measured tools. An
alternative that starts workers as separate processes requires process-tree
accounting before using this runner. Invalid outputs retain their measurements;
successful process exit alone does not establish conformance or visual equality.
