#!/usr/bin/env bash
set -euo pipefail
umask 022

if [[ $# -lt 3 || $# -gt 4 ]]; then
    printf 'Usage: bash rebuild.sh EMPTY_OUTPUT_DIR INPUT_ARCHIVES_DIR NPM_CACHE_DIR [--offline]\n' >&2
    exit 2
fi
recipe=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd -- "$recipe/../../../.." && pwd)
source_archive="$repo/src/DXFER.Blazor/wwwroot/vendor/planegcs/source-9d26bae.tar.gz"
image=emscripten/emsdk@sha256:e077d54e2b8970575ebc4f185ac1de0b95c05f2b266134d4ba27449af7aebf65
mkdir -p -- "$1" "$2" "$3"
output=$(realpath -- "$1")
inputs=$(realpath -- "$2")
cache=$(realpath -- "$3")
if [[ -n "$(find "$output" -mindepth 1 -print -quit)" ]]; then
    printf 'Output directory must be empty: %s\n' "$output" >&2
    exit 2
fi
if [[ $# -eq 4 && "$4" != --offline ]]; then
    printf 'Only --offline is accepted as a fourth argument.\n' >&2
    exit 2
fi

printf '%s  %s\n' 6641e73b41f638621d5f95ae35dfadb45b42c1e43afbeab613753de851566be3 "$source_archive" | sha256sum --check
(cd "$inputs" && sha256sum --check "$recipe/inputs.sha256")
docker image inspect "$image" >/dev/null
tar -xzf "$source_archive" --strip-components=1 -C "$output"
patch --batch --fuzz=0 -p1 -d "$output" < "$recipe/growth-256m.patch"
docker image inspect "$image" > "$output/builder-image.json"

network=default
npm_args=()
if [[ ${4:-} == --offline ]]; then
    network=none
    npm_args=(--offline)
fi

docker run --rm --platform linux/amd64 --cpus=2 --memory=4g --memory-swap=4g --pids-limit=256 \
    --network="$network" -v "$output:/src" -v "$inputs:/inputs:ro" -v "$cache:/npm-cache" \
    -v "$recipe:/recipe:ro" -w /src "$image" \
    timeout --kill-after=5s 600 bash /recipe/install-bindings-deps.sh "${npm_args[@]}" \
    > "$output/install.log" 2>&1
docker run --rm --platform linux/amd64 --cpus=2 --memory=4g --memory-swap=4g --pids-limit=256 \
    --network=none -v "$output:/src" -v "$inputs:/inputs:ro" \
    -v "$recipe:/recipe:ro" -w /src "$image" \
    timeout --kill-after=5s 900 bash /recipe/build-in-container.sh \
    > "$output/build.log" 2>&1
printf 'Build completed in %s; inspect evidence/output.sha256 and build.log.\n' "$output"
