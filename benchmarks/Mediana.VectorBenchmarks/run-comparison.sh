#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 4 ] || [ "$#" -gt 5 ]; then
    echo 'Usage: run-comparison.sh <before-directory> <after-directory> <fixture-directory> <result-directory> [standard|large|all]' >&2
    exit 1
fi
suite=${5:-standard}
case "$suite" in standard|large|all) ;; *) echo "Unknown suite: $suite" >&2; exit 1 ;; esac
before=$(realpath "$1")
after=$(realpath "$2")
fixtures=$(realpath "$3")
mkdir -p "$4"
results=$(realpath "$4")
export DOTNET_PROCESSOR_COUNT=4
export DOTNET_TieredCompilation=0

run_case() {
    local input=$1 size=$2 position=$3 rounds=$4 concurrency=$5 mode=$6 scale=${7:-2}
    local name="$(basename "$input")-${size}-s${scale}-${position}-${concurrency}-${mode}"
    for repetition in 1 2 3; do
        local versions=(before after)
        if [ "$repetition" = 2 ]; then versions=(after before); fi
        for version in "${versions[@]}"; do
            local binaries=$before
            if [ "$version" = after ]; then binaries=$after; fi
            local output="$results/$name/$repetition-$version"
            if [ -e "$output.json" ]; then echo "Result already exists: $output.json" >&2; exit 1; fi
            mkdir -p "$(dirname "$output")"
            dotnet "$binaries/Mediana.VectorBenchmarks.dll" "$fixtures/$input" "$size" "$scale" "$position" "$rounds" "$concurrency" "$mode" "$output" > "$output.json"
        done
        cmp "$results/$name/$repetition-before/output.rgba" "$results/$name/$repetition-after/output.rgba"
        if [ "$mode" = preview ]; then cmp "$results/$name/$repetition-before/output.webp" "$results/$name/$repetition-after/output.webp"; fi
    done
}

if [ "$suite" != large ]; then
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
fi

if [ "$suite" != standard ]; then
    for input in Svg/effects-preview.svg Tgs/orbit.tgs Tgs/effects.tgs Svg/vector-preview.svg Tgs/geometry.tgs; do
        run_case "$input" 1280 .5 5 1 render
    done
    for input in Svg/effects-preview.svg Tgs/effects.tgs; do
        run_case "$input" 2560 .5 5 1 render 1
        run_case "$input" 1280 .5 5 2 render
    done
    run_case Tgs/trimmed-groups-blur.tgs 1280 .75 3 1 render
    run_case Tgs/trimmed-translucent-groups.tgs 1280 .75 5 1 render
    run_case Svg/blur-transformed.svg 200 .5 20 1 render
    run_case Svg/blur-transformed.svg 200 .5 15 4 render
    run_case Svg/blur-transformed.svg 640 .5 3 1 render
fi
