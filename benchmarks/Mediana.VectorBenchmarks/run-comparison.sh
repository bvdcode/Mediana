#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 4 ]; then
    echo 'Usage: run-comparison.sh <before-directory> <after-directory> <fixture-directory> <result-directory>' >&2
    exit 1
fi
before=$(realpath "$1")
after=$(realpath "$2")
fixtures=$(realpath "$3")
mkdir -p "$4"
results=$(realpath "$4")
export DOTNET_PROCESSOR_COUNT=4
export DOTNET_TieredCompilation=0

run_case() {
    local input=$1 size=$2 position=$3 rounds=$4 concurrency=$5 mode=$6
    local name="$(basename "$input")-${size}-${position}-${concurrency}-${mode}"
    for repetition in 1 2 3; do
        local versions=(before after)
        if [ "$repetition" = 2 ]; then versions=(after before); fi
        for version in "${versions[@]}"; do
            local binaries=$before
            if [ "$version" = after ]; then binaries=$after; fi
            local output="$results/$name/$repetition-$version"
            if [ -e "$output.json" ]; then echo "Result already exists: $output.json" >&2; exit 1; fi
            mkdir -p "$(dirname "$output")"
            dotnet "$binaries/Mediana.VectorBenchmarks.dll" "$fixtures/$input" "$size" 2 "$position" "$rounds" "$concurrency" "$mode" "$output" > "$output.json"
        done
        cmp "$results/$name/$repetition-before/output.rgba" "$results/$name/$repetition-after/output.rgba"
        if [ "$mode" = preview ]; then cmp "$results/$name/$repetition-before/output.webp" "$results/$name/$repetition-after/output.webp"; fi
    done
}

for input in Svg/effects-preview.svg Tgs/orbit.tgs Tgs/effects.tgs; do
    for mode in render preview; do run_case "$input" 200 .5 40 1 "$mode"; done
    run_case "$input" 200 .5 30 4 render
    run_case "$input" 64 .5 40 1 render
    run_case "$input" 640 .5 12 1 render
done
for input in Svg/vector-preview.svg Tgs/geometry.tgs Tgs/trimmed-groups-blur.tgs Tgs/trimmed-translucent-groups.tgs; do
    run_case "$input" 200 .25 20 1 render
    run_case "$input" 640 .75 6 1 render
done
for position in 0 .9; do run_case Tgs/effects.tgs 200 "$position" 30 1 render; done
