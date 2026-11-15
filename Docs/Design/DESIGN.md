# AI StoryBuilders — Design Specification

> Where this file disagrees with [the plan](../AIStoryBuildersV2-Plan.md) or [UI-Behavior-Spec.md](../UI-Behavior-Spec.md), they win. UI-Behavior-Spec §19 lists every resolved conflict; for example, the Workspace is a page, not a dialog, and the Manuscript Editor is the Chapters tab.

This folder is the **source of truth for the UI's look** of AIStoryBuildersV2. An AI (or a developer)
should implement the Blazor UI in `AIStoryBuildersV2.Shared` to match the screens described here.

## Files

| File | Screen | Notes |
|------|--------|-------|
| `Library.dc.html` | **Library / Home** (+ Settings, Logs, Utility) | App entry point |
| `StoryWorkspace.dc.html` | **Story Workspace** (a page with tabs; the artboard draws it as a dialog) | The main editing surface |
| `ManuscriptEditor.dc.html` | **Manuscript Editor** (single page) | The "Chapters" experience, full-screen |
| `canvas.json` | Canvas index | Positions/titles of the three artboards |

The `.dc.html` files are interactive **Design Component** artboards (a `<x-dc>` root plus a
`class Component extends DCLogic` script). Read them as structure + behaviour references: the
markup shows the exact layout and the script's `renderVals()` shows state, data shape and
interactions. They were authored to mirror the current AIStoryBuilders app, restyled.

> The same design is also published as a live, clickable artifact:
> https://claude.ai/artifact/UiAs46XY9LriNYAAQdaPLf

## Design tokens

Defined on the root of every artboard; reuse these as CSS variables (see
`AIStoryBuildersV2.Shared/wwwroot/app.css`, which already seeds `--asb-*`).

```
--accent:      #5B4FD6   (primary purple)
--accent-soft: #efecfb   (tint / selected background)
--accent-line: #ddd6f6
--ink:         #1c1a27   (text)
--muted:       #6b6676   (secondary text)
--paper:       #fffdf9   (manuscript surface)
--bg:          #edebf2   (app background)
--panel:       #ffffff
--line:        #e4e1ec   (borders)
```

Fonts: **Inter** for UI, **Newsreader** (serif) for manuscript/body prose and entity names.
Newsreader is not vendored yet, so prose falls back to `Georgia, serif` until a `woff2` is added
(plan §14 Q12).

### Radzen-style button palette (matches the app's `ButtonStyle`)

| Class | Color | Maps to |
|-------|-------|---------|
| `rz-primary` | `--accent` | `ButtonStyle.Primary` |
| `rz-success` | `#1F7A46` | `ButtonStyle.Success` (New / Save / Add) |
| `rz-danger`  | `#BE123C` | `ButtonStyle.Danger` (Delete) |
| `rz-light`   | `#ECEBF0` | `ButtonStyle.Light` (Edit / Select) |
| `rz-warning` | `#B7791F` | `ButtonStyle.Warning` (Re-load) |
| `rz-info`    | `#1C7CD6` | `ButtonStyle.Info` (Import docx/pdf/txt) |
| `rz-ai`      | `#B45309` | the AI (`app_registration`) action |
| `rz-dark`    | `#2C2A36` | Undo |

The real app uses **Radzen** components and **Material icon** names (e.g. `app_registration`,
`note_add`, `save`, `exit_to_app`, `mic`, `undo`, `delete`, `publish`, `description`, `print`,
`save_alt`, `sync`). Prefer the real Radzen components when implementing; the artboards use plain
HTML + inline SVG only because they are static mockups.

---

## 1. Library / Home  (`Library.dc.html`)

Top bar: the AIStoryBuilders logo + wordmark, then a `RadzenMenu`. The artboard shows **Home · Logs ·
Utility · Settings**; the app has **Home · Logs · Settings · Help** (Utility removed, D23; Help added,
plan Q18). The selected menu item swaps the page body. A purple pill link (`--asb-purple`), **Get a
Developmental Edit Report**, sits to the left of the menu (plan Q24).

- **Home** — action row: **New Story** (success, `add`), **Import Story** (light, `publish` — opens
  a `.stybld` project), **Import docx / pdf / txt** (info, `description` — manuscript import with a
  progress bar). Below: a grid of **story cards**, each a cover + title + meta + **Select** button.
  New Story opens the New Story dialog (Title ≤20 chars, Synopsis ≤1000, Chapters 1–3, AI Model,
  **Create Story**).
- **Settings** — AI Provider (`OpenAI`, `Azure OpenAI`, `Anthropic`, `Google AI`, `Ollama`; the
  artboard omits Ollama, UI-Behavior-Spec §6 has the full form), API Key, optional
  Azure Endpoint + Api Version (or an Ollama endpoint), Default AI Model (with a **refresh** button that lists models from
  the provider), a note that *embeddings are handled locally*, and **Save**. Before a key is entered,
  a "get an API key" secondary button links to the provider's console.
- **Logs** — **Export Log** / **Clear Log** + a table of log entries (date + message).
- **Utility** — *Removed in V2 (plan D23): fine-tuning is no longer supported. The artboard's Utility view is not built.*

## 2. Story Workspace  (`StoryWorkspace.dc.html`)

A tabbed surface (opened for one story). Tabs, left→right:
**Details · Timelines · Locations · Characters · Chapters · Knowledgebase · Chat**.
**Chat** is the last tab, to the right of **Knowledgebase** (plan Q19). The artboard in
`StoryWorkspace.dc.html` still shows the older order; the app follows this list.

- **Details** — Story Title (read-only), **Style** dropdown, **Synopsis**, collapsible **System
  Message** and **World Facts** panels. Buttons: **Save** (`save`), **Save Word Doc** (`print`),
  **Export Project** (`save_alt`), **Delete** (danger), **Re-load Story** (warning, `sync` — rebuilds
  embeddings + knowledge graph).
- **Timelines / Locations / Characters** — a **New …** (success) button + a list of cards, each with
  **Edit**. Edit opens a dialog:
  - *Character*: Character Name; Background Type dropdown + **Add Background**; a list of background
    entries (description + timeline); **Save** / **Delete**.
  - *Location*: **New Description**; Location Name; description entries; **Save** / **Delete**.
  - *Timeline*: Timeline Name; Start/Stop date-times; **Save** / **Delete**.
- **Chapters** — **this tab IS the Manuscript Editor** embedded (see §3): the chapter dropdown,
  auto-detected paragraph outline, the document, and the paragraph-edit popup.
- **Knowledgebase** — a knowledge-graph wiki (modeled on
  https://github.com/ADefWebserver/KnowledgeGraphBuilder):
  - **Pages / Graph** toggle.
  - *Pages*: **Explorer** (page tree with New page / New folder / Refresh / Collapse-all + search),
    a Markdown **Editor** (title + breadcrumb, **Design / Source / Preview** modes, clickable
    `[[wiki-links]]`, Revert / Save), and a **Links** panel (Outgoing / Backlinks / Unresolved,
    each unresolved link offering **Create**).
  - *Graph*: an interactive node-link graph grouped by type (Characters / Locations / Timelines,
    plus dashed "missing" nodes), with a scope toggle, a find box, and a node details panel
    (Open / Show around).
- **Chat** — "Story Chat Assistant": model dropdown + **Include Knowledgebase** + clear; user/AI
  message bubbles; proposed-change cards with **Apply** / **Discard**; input + Send. (Backed by a
  knowledge-graph-aware chat over the story. With Include Knowledgebase checked it can also edit
  every Knowledgebase page and manage its folders, each change shown as a proposed-change card;
  deleting a page or folder works even when it is unchecked.)

## 3. Manuscript Editor  (`ManuscriptEditor.dc.html`)

A single page that shows **one chapter at a time**. Left: a **Chapter outline** — a chapter
dropdown and a tree of the chapter's paragraphs ("Section 1 … N" with a snippet). Center: the
manuscript "paper" with the chapter header (eyebrow + title), the chapter tools (**New Chapter**,
**Save**, **Add**, **Delete**), a **Synopsis** field, and the chapter's paragraphs. Selecting a
paragraph reveals **Edit section**, which opens the **paragraph editor** popup — a faithful copy of
the real `ParagraphEdit` toolbar:

`🎤 Speech-to-text · AI (orange, app_registration) · Characters · Location · Timeline · Undo
(after an AI generate) · Delete · Add (note_add) · Save ▾ (with "Autodetect Attributes") · Close`

over a text area. **AI** opens the `ParagraphPrompt` dialog (prompt instructions ≤500, Number of
words {10,20,50,100,200,300,500,800,1000}, AI Model, **Save**). **Characters / Location / Timeline**
open their selector sub-panels.

Paragraphs are called **"Section"** in the UI (matching the real app's confirm dialogs).

---

## Implementation notes

- Target `AIStoryBuildersV2.Shared` (Razor class library) so both the MAUI Windows app and the Web
  (WASM) app get the UI. Host-specific services (file storage, settings persistence, AI key storage)
  are registered per host (see `MauiProgram.cs` and `AIStoryBuildersV2.Web/Program.cs`); shared UI
  services go in `Shared/Hosting/ServiceCollectionExtensions.cs`.
- The existing `ADefWebserver/AIStoryBuilders` app is the behavioural reference for the domain
  (story/chapter/paragraph model, manuscript parsing, multi-provider AI, knowledge-graph build).
- `KnowledgeGraphBuilder` (the repo this solution structure is copied from) is the reference for the
  Knowledgebase tab and for MAUI+WASM+Shared patterns.
