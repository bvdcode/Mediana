# Whole-operation media comparisons

This Linux runner compares complete files in fresh processes, alternates execution
order, records elapsed time, process CPU time and peak RSS, and validates every
decoded sample outside the measured interval. See the
[workload registry and measurements](../../docs/performance/README.md).

The runner uses Python 3.12+, NumPy and Pillow for separate validation, `taskset`, GNU time,
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
codec threads and source color-profile passthrough. `workloads.json` selects
nearest-neighbor chroma in both callers. Their current default is bilinear;
Mediana additionally exposes bicubic and Lanczos3. Different RGB values
are recorded by maximum error and RMSE, never silently accepted as byte-identical.
API definitions are in the
[libheif 1.23.2 source](https://github.com/strukturag/libheif/tree/v1.23.2/libheif/api/libheif).

## Run

Install validation dependencies with
`python3 -m pip install -r benchmarks/Mediana.MediaComparison/requirements.txt`.
Install GNU time as the `time` executable, or supply its absolute path through
`--variable resource_timer=<executable>`.

Use the filenames and input hashes in the measurement report. `--case` is
repeatable. Select two available logical CPUs on different physical cores. The
scratch directory must have space for the largest decoded pair, approximately
1.6 GB for the current HEIC case. A pair is validated and deleted before the next
case; repetitions do not accumulate decoded files. The 30-minute 10 Mbit/s
transcode needs about 5 GB for its encoded pair, in addition to the inputs.
Logs and JSON remain in a new
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
its imports and buffers out of subsequent measured children. Every repeat hashes
the complete output. A byte-identical pair reuses its first full sample comparison
within the same case and immutable input; changed output requires a new comparison.
The validation record identifies the source repetition and method.

CPU is Linux `wait4` user + system time for the specific child, including the
GNU time launcher and descendants it waits for. Peak RSS is GNU time's child
resident high-water mark, converted from KiB to bytes; it does not sum simultaneous descendants.
Capturing RSS from the native launcher excludes Python memory inherited before
the first executable starts. A regression check retains 64 MiB in the parent
and requires the small child's reported peak to remain below half that amount.
It includes runtime/native memory and is not a managed-allocation counter or peak
system RAM. These counters follow the
[Linux resource-usage contract](https://man7.org/linux/man-pages/man2/getrusage.2.html)
and [GNU time](https://www.gnu.org/software/time/).
Elapsed time includes startup, I/O and cleanup. File hashing and validation happen
after timing. A CPU allowance does not establish equal internal parallelism.

The tools are invoked directly. An alternative using detached workers, or a case
requiring aggregate process-tree memory, needs additional accounting. Invalid outputs retain their measurements;
successful process exit alone does not establish conformance or visual equality.

## Additional operations

`Operations` is a Native AOT consumer of public video, Ogg, spreadsheet and
animation APIs. It drains complete streams and releases owned frames. Video
output contains SHA-256 of every visible YUV plane, with its presentation clock;
this measures **decoding plus hashing**, rather than decoder work alone. WebP and
TGS output contains every composited straight RGBA frame. Spreadsheet output
contains every numeric value as little-endian binary64, including stored formula
results; both readers buffer a row before asynchronous output.

```sh
dotnet publish benchmarks/Mediana.MediaComparison/Operations -c Release -r linux-x64 -o artifacts/comparison/operations
dotnet build benchmarks/Mediana.MediaComparison/SpreadsheetReference -c Release
cc -std=c11 -O2 -Wall -Wextra -Werror benchmarks/Mediana.MediaComparison/webp_animation_reference.c \
  $(pkg-config --cflags --libs libwebpdemux) -o artifacts/comparison/webp-animation-reference
cc -std=c11 -O2 -Wall -Wextra -Werror benchmarks/Mediana.MediaComparison/tgs_reference.c \
  $(pkg-config --cflags --libs rlottie) -lz -o artifacts/comparison/tgs-reference
```

`SpreadsheetReference` uses ExcelDataReader 3.9.0 under .NET 10 JIT. The C
references use libwebp's complete-canvas animation decoder and rlottie's
synchronous renderer. The latter converts premultiplied ARGB to straight RGBA
before writing. Reference packages are confined to the comparison projects.

`workloads-images.json`, `workloads-extra.json`, `workloads-video.json` and
`workloads-copy.json` contain explicit commands for additional series. Supply
their executable variables through `--variable`. `Command.output` identifies the
actual product filename; the runner keeps it in place so relative HLS segment
references remain valid. HLS byte counts and fingerprints include every segment,
and a finalized VOD playlist is required. Use `soffice.bin` directly, with a fresh
`UserInstallation` profile per invocation, for LibreOffice comparisons.

Image validation converts declared ICC profiles to sRGB outside timing. Encoding
comparisons also measure decoded error against the common source image. Transport
validation decodes every picture against the original by frame order, checks
access-unit counts and records source PSNR in dB. Copied AAC is fingerprinted with
canonical ADTS headers. Frame-order PSNR does not establish timestamp equality.

Input preparation is separate from timing. Install `requirements-inputs.txt` for
the authored Office fixtures. `prepare_images.py` creates photograph variants,
a dense MMR page and a vector scene; `prepare_documents.py` creates fixed-geometry
DOCX/PPTX and a 400,000-cell XLSX with 80,000 cached formulas.
`prepare_webp_animation.py` encodes complete reference RGBA frames. Input geometry,
reference versions, preparation options and hashes belong in each result report.
## H.264 encoder comparison input

The operation consumer accepts progressive, even-sized 8-bit YUV420 YUV4MPEG2 input
through the public encoder and MPEG-TS writer APIs:

```sh
Mediana.OperationComparison h264-encode input.y4m output.ts 26 0 48
```

Arguments select QP, target bits per second (zero selects fixed QP), and the IDR
interval in frames. Input uses an explicit frame rate, square or declared sample
aspect ratio, and center or left chroma location. The comparison assigns limited
range BT.709 color interpretation to these samples; prepare inputs accordingly.
The consumer rejects unsupported or truncated headers and frame tags, reads to
EOF, checks encoder completion and reports CPU, elapsed time, process peak RSS,
managed allocation totals and encoded packet bytes. It checks that all owned
media storage has been released. This separates encoding from source decoding;
whole-operation transcode measurements remain a separate workload.
