# Test data and expected results

This file holds the fixtures and expected outcomes for the *done when* checks in [AIStoryBuildersV2-Plan.md](AIStoryBuildersV2-Plan.md) §12.
File formats follow [V1-File-Format.md](V1-File-Format.md). The story is based on the sample data in the design artboards ("Forgetting Baldur").
Everything here is fictional.

| Section | Used by |
|---|---|
| §1 Fixture story `before/` | T2a–T2d, T8, T9a, T10 |
| §2 Deterministic fixture vectors | T2a, T2b |
| §3 Golden operations (V1 `after-<operation>/`) | T2b–T2d |
| §4 Reader-tolerance fixtures | T2a |
| §5 Probe fixtures | T6 |
| §6 Knowledge Base fixture | T14–T16 |
| §7 Expected AI outcomes | T13, T16, plan Q19 and Q20 (chat KB edits, tab order) |
| §8 Embedding self-test sentences | T4 |

---

## 1. Fixture story: `AIStoryBuildersV2.Tests/Fixtures/V1Story/before/`

The folder mirrors `Documents\AIStoryBuilders` (the root), containing one story. `FixtureFactory.WriteBefore(string root)` in the Tests project (T2a) generates it from the tables below. The generated files are also committed, so a person can copy them into V1 (T2b). Regenerating must give byte-identical files.

**Generation rules**, matching V1's own writers (format spec §3.0 and §3.6–§3.10):

- UTF-8 without BOM.
- `[VEC:<text>]` means the fixture vector of `<text>` (§2), written as `[` + 384 values joined by `,` + `]`.
- Files V1 writes with `WriteAllLines` (`AIStoryBuildersStories.csv`, `Timelines.csv`, `Characters/*.csv`, `Locations/*.csv`): every line ends with CRLF, including the last.
- Character and location files have a **blank line after each record**, as V1's UI writes them.
- Chapter and paragraph files have **no trailing newline**, as with V1's `WriteAllText`.
- Dates use V1's short form in **en-US**. Tests that read them set `CultureInfo.CurrentCulture = en-US`.
- `AIStoryBuildersLog.csv` and `AIStoryBuildersDatabase.json` are not part of the fixture.

### 1.1 `AIStoryBuildersStories.csv`

```
1|Forgetting Baldur|Fantasy|You are a software program that creates prose for novels.|Balt searches for the father who vanished after the battle at the ridge.|Ravens carry messages for Odin.
```

### 1.2 `Forgetting Baldur/Timelines.csv`

```
Years Before|The battle at the ridge and its aftermath.|11/1/2026 6:00 AM|11/2/2026 6:00 AM
Present Day|Balt's search begins.|11/3/2026 6:00 AM|11/4/2026 6:00 AM
```

### 1.3 `Forgetting Baldur/Characters/`

| File | Lines (each followed by a blank line) |
|---|---|
| `Balt.csv` | `Appearance\|\|Tall, with a scar across his left cheek.\|[VEC:Tall, with a scar across his left cheek.]`<br/>`History\|Years Before\|Fought beside his father at the ridge.\|[VEC:Fought beside his father at the ridge.]` |
| `Freya.csv` | `Appearance\|\|Red hair, always carries a bow.\|[VEC:Red hair, always carries a bow.]` |
| `Father.csv` | `History\|Years Before\|Vanished after the battle at the ridge.\|[VEC:Vanished after the battle at the ridge.]` |

### 1.4 `Forgetting Baldur/Locations/`

| File | Lines (each followed by a blank line) |
|---|---|
| `Ridge.csv` | `A windswept ridge above the fjord.\|\|[VEC:A windswept ridge above the fjord.]` |
| `Village.csv` | `A cluster of turf houses by the water.\|Present Day\|[VEC:A cluster of turf houses by the water.]` |

### 1.5 `Forgetting Baldur/Chapters/`

| File | Content (one line, no trailing newline) |
|---|---|
| `Chapter1/Chapter1.txt` | `The battle at the ridge, and Father's disappearance.\|[VEC:The battle at the ridge, and Father's disappearance.]` |
| `Chapter1/Paragraph1.txt` | `Ridge\|Years Before\|[Balt,Father]\|Balt and his father held the ridge until dusk.\|[VEC:Balt and his father held the ridge until dusk.]` |
| `Chapter1/Paragraph2.txt` | `Ridge\|Years Before\|[Balt]\|When the smoke cleared, Father was gone.\|[VEC:When the smoke cleared, Father was gone.]` |
| `Chapter1/Paragraph3.txt` | `Village\|Present Day\|[Balt,Freya]\|Years later, Freya found Balt mending nets.\|[VEC:Years later, Freya found Balt mending nets.]` |
| `Chapter2/Chapter2.txt` | `Balt and Freya set out to learn the truth.\|[VEC:Balt and Freya set out to learn the truth.]` |
| `Chapter2/Paragraph1.txt` | `Village\|Present Day\|[Balt,Freya]\|Freya handed Balt a raven feather.\|[VEC:Freya handed Balt a raven feather.]` |
| `Chapter2/Paragraph2.txt` | `Ridge\|Present Day\|[Freya]\|At the ridge, Freya found a silver necklace in the heather.\|[VEC:At the ridge, Freya found a silver necklace in the heather.]` |

There is no `Graph/` folder and no `KnowledgeBase/` folder in `before/`.

### 1.6 Expected reads (T2a)

| Call | Expected |
|---|---|
| `GetStorysAsync()` | 1 story: Title `Forgetting Baldur`, Style `Fantasy`, WorldFacts `Ravens carry messages for Odin.` |
| `GetTimelines` | `Years Before` (Start 2026-11-01 06:00), then `Present Day`, sorted by Start |
| `GetCharacters` | `Balt` (2 backgrounds), `Father` (1), `Freya` (1). Compare sorted by name, because V1 reads in OS order. |
| `GetLocations` | `Ridge` (timeline `""`), `Village` (timeline `Present Day`) |
| `GetChapters` | `Chapter 1` (Sequence 1) and `Chapter 2` (Sequence 2), with the synopses above |
| `GetParagraphs(Chapter 1)` | 3 paragraphs. Paragraph 3: Location `Village`, Timeline `Present Day`, Characters `[Balt, Freya]` |

---

## 2. Deterministic fixture vectors

`FixtureVector(string text) : float[384]`, in the Tests project:

1. `seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0)`
2. `rng = new Random(seed)`. A seeded `System.Random` uses the legacy algorithm, which is stable across .NET versions.
3. `v[i] = (float)(rng.NextDouble() * 2 - 1)` for `i` in `0..383`
4. L2-normalize `v`.
5. Write each value with `float.ToString(CultureInfo.InvariantCulture)`: shortest round-trip form, exponent form allowed.

Production code never uses this. Tests use it through `FakeEmbeddingGenerator`, an `IEmbeddingGenerator<string, Embedding<float>>` that returns `FixtureVector(text)`.

---

## 3. Golden operations (T2b, one-time manual step)

A person runs each operation **in V1** on a fresh copy of `before/` and commits the result. Do it on a machine where V1 runs with the **en-US** culture. **Back up `Documents\AIStoryBuilders` first** (see Dev-Environment.md §5).

For each operation:

1. Copy `before/Forgetting Baldur/` into `Documents\AIStoryBuilders\`. Add the `before/` row to that folder's `AIStoryBuildersStories.csv`, keeping your other rows.
2. Start V1, open **Forgetting Baldur**, and perform the steps.
3. Close V1. Copy `Documents\AIStoryBuilders\Forgetting Baldur\` to `Fixtures/V1Story/after-<operation>/Forgetting Baldur/`. Copy the story's row to `after-<operation>/AIStoryBuildersStories.csv`.
4. Delete the fixture story from V1 (or restore your backup) before the next operation.

| Operation folder | Steps in V1 | What V2's test does | Main expected differences from `before/` |
|---|---|---|---|
| `after-add-paragraph` | Chapters tab → Chapter 1 → open Paragraph 2 → **Add** → confirm **Yes** ("Add Section above this Section"). Close without typing. | `AddParagraph(chapter1, new Paragraph { Sequence = 2, Location = "Ridge", Timeline = "Years Before", Characters = [Balt] })` | New `Paragraph2.txt` is `Ridge\|Years Before\|[Balt]\|\|`. The old 2 and 3 become 3 and 4. |
| `after-rename-character` | Characters tab → Freya → Edit → Name `Freyja` → Save → confirm the rename | `UpdateCharacterNameAsync(freyja, "Freya")`, after the save V1 does first | `Characters/Freyja.csv`. **Every** paragraph file is rewritten with a trailing CRLF. `Freya` becomes `Freyja` in the lists and the prose. Changed prose is re-embedded. |
| `after-rename-timeline` | Timelines tab → Present Day → Edit → Name `Now` → Save → confirm | `UpdateTimelineAndTimelineNameAsync(now, "Present Day")` | `Timelines.csv` is rewritten. The paragraph timeline field becomes `Now`. **Every** character and location file is rewritten and re-embedded. `Village.csv`'s timeline becomes `Now`. |
| `after-delete-chapter` | Chapters tab → Chapter 1 → **Delete** → confirm | `DeleteChapter(chapter1)` and then restructure, as V1 does | Only `Chapter1/` remains. It holds the old Chapter 2 content, with its file renamed to `Chapter1.txt`. |

**Comparer rules (`V1TreeComparer`):**

- Compare every file byte for byte, **except**:
  - `Graph/**` is ignored, because V1 may build it when a story is opened
  - in `[...]` vector fields, only the format is checked: 384 numbers that parse with the invariant culture
- `AIStoryBuildersStories.csv` is compared on the fixture row only, ignoring the Id field. V1 assigns Ids by row count.
- If V1 did not create a `Graph/` folder, its absence is fine.

Commit `Fixtures/V1Story/README.md` with the date, the V1 version and the culture used.

---

## 4. Reader-tolerance fixtures: `Fixtures/V1Quirks/`

V2 readers must handle bad records one at a time (format spec §7). Generate these in the test, not from V1.

| Input | Expected V2 read |
|---|---|
| A paragraph written by `UpdateParagraph` with CRLF inside the content: `Ridge\|Present Day\|[Freya]\|Line one.␍␊Line two.\|[VEC:x]` | Content `Line one.\nLine two.`, with the vector taken from the last field |
| A paragraph from manuscript import with LF and a blank line: `Unknown\|Unknown\|[]\|A.␊␊B.\|[VEC:x]` | Content `A.\n\nB.` (V2 keeps blank lines; V1 drops them). Location and timeline are `Unknown`. Characters: empty list. |
| Character list `]`, as `AddStory` writes it | Empty list |
| Character list `[Balt, Freya,]` | `Balt`, `Freya` (trimmed, empty entries dropped) |
| Timeline row with V1's `AddTimeline` "G" form: `X\|d\|11/5/2026 3:04:05 PM\|` | Start 2026-11-05 15:04:05, Stop null |
| Timeline start with U+202F before `PM` | Parses the same as with a normal space |
| A `Timelines.csv` with one row whose Start doesn't parse | **That row** is skipped and logged; the other rows load (V1 drops them all) |
| A location line with no `\|` | That record is skipped; the other records and locations load |
| A stories row with 5 fields (from `.stybld` import) | WorldFacts `""` |
| A stories line ending with `\r\r` (after V1's `DeleteStory`) | The trailing CRs are stripped from the last field |
| `Chapters/Notes/` (not `Chapter{N}`) | Ignored with a log entry; the real chapters load |
| `Paragraph1 - Copy.txt` | Ignored with a log entry |
| `Manifest.config` inside the story folder | Ignored by readers. Not copied back into the folder on import. Never included in V2's export (the fresh manifest wins). |
| A vector serialized under de-DE: `[0,0123,-0,0456,...]` | The vector is treated as missing (the count isn't 384), and it is re-embedded on the next Re-load |

**Write rules (V2 → V1 compatibility):**

- Delete `|` from every free-text field before writing.
- Replace line breaks with a space in every field except paragraph content.
- Write paragraph content line breaks as CRLF, as `UpdateParagraph` does.
- Write character lists as `[a,b]`, or `[]` when there are none, never `]`.
- These rules are in plan D32.

---

## 5. Probe fixtures (T6, `ProbeFixtures`)

Plan §5.9. Every string is exact.

**System prompt (all stages):**

```
You are being tested for tool use. Follow the instructions exactly.
```

**Connect stage, user message:** `Reply with OK.`

**Stage A, tool declaration:**

| Name | Description | Parameters (JSON schema) |
|---|---|---|
| `kb_read_page` | `Reads one knowledge-base page and the pages it links to.` | `{"type":"object","properties":{"path":{"type":"string","description":"Page path without .md"}},"required":["path"]}` |

**Stage A, user message:**

```
Call the kb_read_page tool with path "Characters/Freya". Do not answer in text.
```

Stage A passes when the response contains one `FunctionCallContent` named `kb_read_page` whose `path` argument, after trimming, removing a trailing `.md` and ignoring case, equals `Characters/Freya`.

**Stage B, tool result** (`ProbeFixtures.GraphResult`, the `FunctionResultContent` for stage A's call id):

```json
{"pages":[
 {"path":"Characters/Freya.md","markdown":"---\ntitle: Freya\ntype: character\n---\nFreya is the daughter of [[Odin]] and travels with [[Characters/Balt|Balt]]."},
 {"path":"Characters/Balt.md","markdown":"---\ntitle: Balt\ntype: character\n---\nBalt fears the ravens of [[Odin]] and protects [[Characters/Freya]]."},
 {"path":"Places/Asgard.md","markdown":"---\ntitle: Asgard\ntype: location\n---\nOdin rules here. See [[Characters/Freya]]."}
]}
```

**Stage B, user message:**

```
Do not call tools. Using only the tool result above, return only JSON in the form {"linksToOdin":[...]} listing the paths of pages that contain a [[...]] link to Odin.
```

**Expected:** `{"linksToOdin":["Characters/Freya.md","Characters/Balt.md"]}`. Compare as a set, ignoring case and an optional `.md`. `Places/Asgard.md` is the distractor: it mentions Odin, but not as a link.

**Negative model for T6:** an Ollama model without tool support (for example a small base model). It must fail at `ToolCall`.

---

## 6. Knowledge Base fixture: `Fixtures/KbStory/`

This is `before/` plus a `Forgetting Baldur/KnowledgeBase/` folder. KB pages are UTF-8 without BOM, with LF line endings, the way KGB writes them.

**Not committed as files.** There is no `AIStoryBuildersV2.Tests/Fixtures/KbStory/` folder. For the manual checks in §7, add these pages to a copy of `before/` in the Knowledgebase tab. The unit tests seed their own, shorter pages in code instead (for example `SearchKnowledgeBaseTests.SeedAsync` writes `Lore/Necklace.md` as "forged by Hrafnkel from nine silver beads" and `Hrafnkel.md`), so their expected text differs from this section.

`KnowledgeBase/Lore/Necklace.md`:

```markdown
---
title: The Necklace
tags: [artifact]
---
The silver necklace was forged by the smith **Hrafnkel** from a fallen star during the winter of the long night.
Whoever wears it can hear the ravens of [[Odin]].
It was last seen with [[People/Father]].
```

`KnowledgeBase/People/Father.md`:

```markdown
---
title: Father
---
Balt's father carried the [[Lore/Necklace]] into the battle at the ridge.
```

Expected KB state (T14, T15):

| Check | Expected |
|---|---|
| Pages | 2 (`Lore/Necklace.md`, `People/Father.md`) |
| Backlinks of `Lore/Necklace.md` | `People/Father.md` |
| Unresolved links | `Odin` (from `Lore/Necklace.md`) |
| `.stybld` round trip | Both pages come back byte-identical under `KnowledgeBase/` |
| Another story's KB | Empty: KB pages never leak between stories |

---

## 7. Expected AI outcomes (live tests and manual checks)

LLM output varies, so each check names words that must or must not appear, compared ignoring case.

| Task | Setup | Action | Pass when |
|---|---|---|---|
| T13 | `before/` story, any verified model | Chat: `Who does Balt interact with in chapter 2?` | The answer mentions `Freya`, and the log shows at least one story-graph tool call |
| T16 chat, checked | `Fixtures/KbStory`, Include Knowledgebase **checked** | Chat: `Who forged the necklace?` | The answer contains `Hrafnkel` and a citation `[[Lore/Necklace]]`. The log shows `kb_search_pages` or `kb_read_page`. |
| T16 chat, unchecked | Same, **unchecked** | Same question | No `kb_` read tool is offered or called; only `kb_delete_page` and `kb_delete_folder` are offered (plan Q21). The answer does not contain `Hrafnkel`, which appears nowhere in the story files. |
| Q21 unchecked delete | Same, **unchecked** | Chat: `Delete the page People/Father.md.` | A "Proposed change · kb_delete_page" card appears. After **Apply**, the page is gone from the Knowledgebase tab. Asking to rewrite a page instead gets no card. |
| Q19 chat edits a page | Same, **checked** | Chat: `Add to the Lore/Necklace page that it glows at night.` | A "Proposed change · kb_update_page" card appears and `Lore/Necklace.md` is unchanged. After **Apply**, the Knowledgebase tab shows the page with the new sentence and the rest of the page kept. |
| Q19 Apply after a user save | Same as above, but edit and **Save** `Lore/Necklace.md` in the Knowledgebase tab before clicking **Apply** | — | "Could not apply the change: Lore/Necklace.md changed since the change was proposed." The user's text is kept. |
| Q20 chat renames a folder | `Fixtures/KbStory`, Include Knowledgebase **checked** | Chat: `Rename the Lore folder to Artifacts.` | A "Proposed change · kb_rename_folder" card appears and the tree is unchanged. After **Apply**, the Knowledgebase tab shows `Artifacts/Necklace`, and pages that linked to `[[Lore/Necklace]]` now link to the moved page. |
| Q20 chat deletes a folder | Same | Chat: `Delete the Lore folder.` | The card's summary gives the page and subfolder count. After **Apply**, the folder and its pages are gone and pages outside it are kept. |
| Q19 tab order | Open any story | — | The tabs read Details, Timelines, Locations, Characters, Chapters, Knowledgebase, Chat |
| T16 generate, checked | Chapter 2 → Paragraph 2 → AI | Prompt `Freya studies the necklace and remembers who made it.`, 100 words, Search knowledgebase **checked** | The generated text contains `Hrafnkel` or `fallen star` |
| T16 generate, unchecked | Same, **unchecked** | Same prompt | Success is not judged on content. Check only that the retriever was not called. |
| T16 retrieval failure | Same as checked, with the provider made unreachable during retrieval only (test double) | Generate | A warning toast appears, the paragraph is still generated, and Undo works |

---

## 8. Embedding self-test sentences (T4)

`Fixtures/embeddings.json` holds the vectors `LocalEmbeddingGenerator` produces for these five sentences. The WASM self-test must reach cosine ≥ 0.98 for each one.

1. `Balt and his father held the ridge until dusk.`
2. `The silver necklace was forged from a fallen star.`
3. `Ravens carry messages for Odin.`
4. `A cluster of turf houses by the water.`
5. `` (the empty string)
