#!/usr/bin/env bash
set -euo pipefail

# Publish into a new directory; never replace assemblies under a live process.
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output=${1:?Usage: publish-linux.sh NEW_OUTPUT_DIRECTORY [RUNTIME_IDENTIFIER]}
rid=${2:-linux-x64}
dotnet=${DOTNET:-dotnet}
if [[ -e "$output" ]]; then
    printf 'Refusing existing publish destination: %s\n' "$output" >&2
    exit 1
fi

args=(-c Release -r "$rid" --self-contained true
    -p:PublishProfile=LinuxSelfContained -o "$output")
if [[ "$rid" == ubuntu.* ]]; then
    args+=(-p:UseRidGraph=true)
fi
"$dotnet" publish "$root/src/DXFER.Web/DXFER.Web.csproj" "${args[@]}"
node "$root/scripts/verify-linux-publish.cjs" "$output"
