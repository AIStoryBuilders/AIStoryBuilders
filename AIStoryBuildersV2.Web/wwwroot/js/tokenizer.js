/**
 * WordPiece tokenizer for all-MiniLM-L6-v2 that matches V1's LocalEmbeddingGenerator exactly, so browser vectors match
 * the MAUI app's: lower-case, split on space/tab/CR/LF only, greedy longest-match WordPiece, an unknown remainder becomes
 * [UNK], at most 254 word pieces plus [CLS] and [SEP]. (Online's tokenizer also split punctuation, which V1 does not.)
 */
export class WordPieceTokenizer {
    constructor() {
        this.vocab = null;
        this.maxTokens = 256;
        this.clsId = 101;
        this.sepId = 102;
        this.unkId = 100;
    }

    async load(vocabUrl) {
        const response = await fetch(vocabUrl);
        if (!response.ok) throw new Error(`Failed to load ${vocabUrl}: ${response.status}`);
        const lines = (await response.text()).split('\n');
        this.vocab = new Map();
        for (let i = 0; i < lines.length; i++) {
            const token = lines[i].trim();
            if (token.length === 0 && i === lines.length - 1) continue;
            this.vocab.set(token, i);
        }
        this.clsId = this.vocab.get('[CLS]') ?? 101;
        this.sepId = this.vocab.get('[SEP]') ?? 102;
        this.unkId = this.vocab.get('[UNK]') ?? 100;
    }

    /** @returns {number[]} token ids including [CLS] and [SEP] */
    tokenize(text) {
        const max = this.maxTokens - 2;
        const ids = [];
        const words = text.toLowerCase().split(/[ \t\n\r]+/).filter(w => w.length > 0);
        for (const word of words) {
            if (ids.length >= max) break;
            let remaining = word;
            let first = true;
            while (remaining.length > 0 && ids.length < max) {
                let best = null;
                let bestLen = 0;
                for (let end = remaining.length; end > 0; end--) {
                    const candidate = first ? remaining.substring(0, end) : '##' + remaining.substring(0, end);
                    if (this.vocab.has(candidate)) {
                        best = candidate;
                        bestLen = end;
                        break;
                    }
                }
                if (best === null) {
                    ids.push(this.unkId);
                    break;
                }
                ids.push(this.vocab.get(best));
                remaining = remaining.substring(bestLen);
                first = false;
            }
        }
        return [this.clsId, ...ids, this.sepId];
    }
}
