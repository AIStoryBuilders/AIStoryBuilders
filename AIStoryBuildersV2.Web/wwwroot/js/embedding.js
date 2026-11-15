/**
 * Browser embeddings with ONNX Runtime Web (plan D18), adapted from AIStoryBuildersOnline.
 * onnxruntime-web 1.21.0 and the quantized all-MiniLM-L6-v2 model are vendored in wwwroot, never loaded from a CDN.
 * Every URL is resolved against document.baseURI, so the app also works under a non-root <base href>.
 */
import { WordPieceTokenizer } from './tokenizer.js?v=2'; // ?v=2: V1 Online has a different file at this URL on the same origin.

const HIDDEN = 384;
let ready = null;
let ort = null;
let session = null;
let tokenizer = null;

function url(path) {
    return new URL(path, document.baseURI).href;
}

async function initialize() {
    ort = await import(url('js/ort/ort.wasm.min.mjs'));
    ort.env.wasm.wasmPaths = url('js/ort/');
    // Threads need cross-origin isolation, which a plain static host does not give.
    ort.env.wasm.numThreads = 1;
    session = await ort.InferenceSession.create(url('models/all-MiniLM-L6-v2/model_quantized.onnx'), { executionProviders: ['wasm'] });
    tokenizer = new WordPieceTokenizer();
    await tokenizer.load(url('models/all-MiniLM-L6-v2/vocab.txt'));
}

export function initializeEmbeddingModel() {
    ready ??= initialize().catch(e => { ready = null; throw e; });
    return ready;
}

/** A 384-number, mean-pooled, L2-normalized embedding, as V1's LocalEmbeddingGenerator computes it. */
export async function generateEmbedding(text) {
    await initializeEmbeddingModel();
    const ids = tokenizer.tokenize(text ?? '');
    const n = ids.length;
    const inputIds = BigInt64Array.from(ids, v => BigInt(v));
    const mask = new BigInt64Array(n).fill(1n);
    const types = new BigInt64Array(n);
    const results = await session.run({
        input_ids: new ort.Tensor('int64', inputIds, [1, n]),
        attention_mask: new ort.Tensor('int64', mask, [1, n]),
        token_type_ids: new ort.Tensor('int64', types, [1, n]),
    });
    const hidden = (results['last_hidden_state'] ?? Object.values(results)[0]).data;

    const embedding = new Float32Array(HIDDEN);
    for (let t = 0; t < n; t++) {
        for (let d = 0; d < HIDDEN; d++) embedding[d] += hidden[t * HIDDEN + d];
    }
    let norm = 0;
    for (let d = 0; d < HIDDEN; d++) {
        embedding[d] /= n;
        norm += embedding[d] * embedding[d];
    }
    norm = Math.sqrt(norm);
    if (norm > 0) for (let d = 0; d < HIDDEN; d++) embedding[d] /= norm;
    return Array.from(embedding);
}

export async function generateEmbeddings(texts) {
    const out = [];
    for (const text of texts) out.push(await generateEmbedding(text));
    return out;
}
