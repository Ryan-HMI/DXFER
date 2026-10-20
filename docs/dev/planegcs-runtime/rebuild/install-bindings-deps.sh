#!/usr/bin/env bash
set -euo pipefail
umask 022
mkdir -p /tools
tar -xJf /inputs/node-v20.19.0-linux-x64.tar.xz -C /tools
export PATH="/tools/node-v20.19.0-linux-x64/bin:$PATH"
export npm_config_nodedir=/tools/node-v20.19.0-linux-x64
export npm_config_build_from_source=true
cd /src
npm ci --no-audit --no-fund --cache=/npm-cache "$@"
