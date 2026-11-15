# Docs/Design

The UI design output for AIStoryBuildersV2. Start with **[DESIGN.md](DESIGN.md)** — it describes
every screen, control and interaction, and maps them to the app's features.

The `*.dc.html` files are interactive design artboards (Design Component format). Read them as the
exact layout + behaviour reference; `canvas.json` records how the three boards sit on the canvas.

- `Library.dc.html` — Library / Home (+ Settings, Logs; the artboard's Utility view is not built, plan D23)
- `StoryWorkspace.dc.html` — Story Workspace (Details · Timelines · Locations · Characters ·
  Chapters · Chat · Knowledgebase). The app puts Knowledgebase before Chat, so Chat is the last
  tab (DESIGN.md §2, plan Q19).
- `ManuscriptEditor.dc.html` — the single-page Manuscript Editor

Live version: https://claude.ai/artifact/UiAs46XY9LriNYAAQdaPLf
