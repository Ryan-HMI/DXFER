#!/usr/bin/env bash
set -euo pipefail
umask 022
export LC_ALL=C TZ=UTC SOURCE_DATE_EPOCH=0

cd /src
mkdir -p /inc /deps /src/evidence
mkdir -p /tools
tar -xJf /inputs/node-v20.19.0-linux-x64.tar.xz -C /tools
export PATH="/tools/node-v20.19.0-linux-x64/bin:$PATH"
tar -xjf /inputs/boost_1_85_0.tar.bz2 -C /deps
tar -xzf /inputs/eigen-3147391d946bb4b6c68edd901f2add6ac1f31f8c.tar.gz -C /deps
ln -s /deps/boost_1_85_0/boost /inc/boost
ln -s /deps/eigen-3147391d946bb4b6c68edd901f2add6ac1f31f8c /usr/include/eigen3

emcc --version > evidence/emcc-version.txt
node --version > evidence/node-version.txt
npm --version > evidence/npm-version.txt
cmake --version > evidence/cmake-version.txt
dpkg-query -W > evidence/base-image-packages.txt
sha256sum package-lock.json planegcs/CMakeLists.txt > evidence/source-inputs.sha256

npm run build:bindings
sha256sum planegcs/bindings.cpp > evidence/generated-bindings.sha256
emcmake cmake -S planegcs -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --parallel 2
cp build/bin/planegcs.js build/bin/planegcs.wasm planegcs_dist/
npm exec -- tsc
cp planegcs_dist/planegcs.js planegcs_dist/planegcs.wasm dist/planegcs_dist/
sha256sum dist/planegcs_dist/planegcs.js dist/planegcs_dist/planegcs.wasm > evidence/output.sha256
