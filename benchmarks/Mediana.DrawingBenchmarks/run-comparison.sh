#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

set -euo pipefail
if [ "$#" -ne 4 ]; then
    echo "Usage: $0 <dotnet> <before-binaries> <after-binaries> <results-directory>" >&2
    exit 1
fi
runtime="$1"
before="$2"
after="$3"
results="$4"
mkdir -p "$results"
export DOTNET_PROCESSOR_COUNT=2
export DOTNET_TieredCompilation=0

for repeat in 1 2 3; do
    if [ "$repeat" -eq 2 ]; then versions=(after before); else versions=(before after); fi
    while read -r scene width height scale; do
        for version in "${versions[@]}"; do
            directory="$before"
            if [ "$version" = after ]; then directory="$after"; fi
            "$runtime" "$directory/Mediana.DrawingBenchmarks.dll" "$scene" "$width" "$height" "$scale" 0 \
                "$results/$version-$scene-$width-$height-$scale" >> "$results/$version-$repeat.jsonl"
            echo "$repeat $version $scene ${width}x${height} scale=$scale"
        done
    done <<'CASES'
text 200 200 1
text 2560 2560 1
text 2560 2560 2
text 2560 2560 4
short-text 200 200 1
short-text 2560 2560 4
text-aileron 200 200 1
text-aileron 2560 2560 4
table-empty 200 200 2
table-empty 2560 2560 2
table-full 200 200 2
table-full 2560 2560 2
measure 200 200 1
measure 2560 2560 1
bars 200 200 2
bars 640 480 2
bars 2560 2560 2
curves 200 200 2
curves 1024 768 1
strokes 200 200 1
strokes 1280 960 1
tiles 200 200 2
tiles 640 480 2
CASES
    export DOTNET_PROCESSOR_COUNT=4
    while read -r scene size scale workers iterations; do
        for version in "${versions[@]}"; do
            directory="$before"
            if [ "$version" = after ]; then directory="$after"; fi
            "$runtime" "$directory/Mediana.DrawingBenchmarks.dll" batch "$scene" "$size" "$scale" "$workers" "$iterations" \
                >> "$results/$version-batch-$repeat.jsonl"
            echo "$repeat $version batch $scene size=$size scale=$scale workers=$workers"
        done
    done <<'CASES'
table-full 200 2 1 12
table-full 200 2 2 12
table-full 200 2 4 12
text 2560 4 1 3
text 2560 4 2 3
text 2560 4 4 3
bars 2560 2 1 6
bars 2560 2 2 6
bars 2560 2 4 6
CASES
    export DOTNET_PROCESSOR_COUNT=2
done
