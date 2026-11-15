# Vendored onnxruntime-web

Files from the npm package `onnxruntime-web@1.21.0` (`dist/`), MIT license, Copyright (c) Microsoft Corporation.
Only the WASM-only build is kept: `ort.wasm.min.mjs`, `ort-wasm-simd-threaded.mjs` and `ort-wasm-simd-threaded.wasm`.
They are vendored so no third-party script runs in an origin that holds the API key (plan D18, §9).

The model in `../../models/all-MiniLM-L6-v2/` is the quantized sentence-transformers/all-MiniLM-L6-v2 (Apache-2.0),
copied from AIStoryBuildersOnline.
