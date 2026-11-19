# Compression workloads

This harness compares buffered PNG encoding, PNG streaming to a non-seekable sink,
strip-based TIFF decoding and PDF Flate image decoding through public APIs.
It reports wall time, process CPU, total managed allocations, process peak RSS
and the output SHA-256. Warmup and hashing are outside the measured interval;
RSS includes the entire process lifetime. Every operation checks media ownership.

Generate deterministic gradient and noise inputs with the Python standard library:

```sh
python3 benchmarks/Mediana.CompressionBenchmarks/generate_inputs.py artifacts/compression-input
```

Build the control before editing the libraries, keeping its complete output directory,
and then build the candidate into another directory. Use the same SDK, runtime and
input files for both. Alternate fresh processes and compare medians and ranges.

```sh
dotnet build benchmarks/Mediana.CompressionBenchmarks -c Release -o artifacts/compression-before
dotnet artifacts/compression-before/Mediana.CompressionBenchmarks.dll png artifacts/compression-input/512-gradient-rows64.tif 100
dotnet artifacts/compression-before/Mediana.CompressionBenchmarks.dll png-stream artifacts/compression-input/1280-noise-rows64.tif 10
dotnet artifacts/compression-before/Mediana.CompressionBenchmarks.dll tiff artifacts/compression-input/512-gradient-rows1.tif 100
dotnet artifacts/compression-before/Mediana.CompressionBenchmarks.dll pdf artifacts/compression-input/images-small.pdf 100
```

PNG hashes cover the complete PNG file. TIFF hashes cover RGBA pixels. PDF hashes
cover every decoded image on the first page, in interpretation order. Inputs are
synthetic workloads for comparing implementations; they do not establish format
conformance or replace tests against independent image references.

See [span compression and portable vector transform results](../../docs/compression-performance.md).
