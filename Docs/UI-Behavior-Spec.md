# AI StoryBuilders V2: UI Behavior Specification

| | |
|---|---|
| Status | Draft for implementation |
| Date | 2026-11-05 |
| Scope | Every control in the redesigned UI ([Design/DESIGN.md](Design/DESIGN.md)), mapped to concrete behavior |
| Source of truth | Layout and colors: the artboards. Fields: DESIGN.md. Behavior: V1. Decisions: [AIStoryBuildersV2-Plan.md](AIStoryBuildersV2-Plan.md) (cited as "Plan §x" or "Dx"). Where they conflict, see [§19](#19-design-vs-v1-conflicts-and-how-they-were-resolved). |
| Audience | A developer or agent implementing `AIStoryBuildersV2.Shared` |

## 0. How to read this spec

### 0.1 String markers

| Marker | Meaning |
|---|---|
| `"text"` with no marker | Exact V1 UI string. Copy it character for character. |
| **(design)** | Exact text from an artboard |
| **(KGB)** | Exact text from KnowledgeGraphBuilder |
| **(plan)** | Text decided in the Plan |
| **(new)** | Invented for V2 by this spec. Anything else marked **(new)** (a rule, a button, a behavior) is also an invention. |

### 0.2 Notation

- **Toast** = `NotificationService.Notify`, written as `Severity · "Summary" / "Detail" · duration`. Example: `Success · "Saved" / "The file was saved successfully." · 4 s`.
- **Confirm** = `DialogService.Confirm(text, title, new ConfirmOptions { OkButtonText = "Yes", CancelButtonText = "No" })`, written as **title** / text. Every V1 confirm uses Yes / No; keep that.
- **KGB choice dialog** = `DialogExtensions.ChooseAsync` ([DialogExtensions.cs](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Components/Common/DialogExtensions.cs)).
- **Style/icon** = Radzen `ButtonStyle` · Material icon name. `—` means no icon.
- Service calls name the V2 contract from Plan §6. `Svc` = `AIStoryBuildersService`, `Orch` = `OrchestratorMethods`, `Settings` = `IAppSettingsService`, `Persist` = `IStoryPersistence`, `Export` = `IFileExportService`, `Host` = `IHostCapabilities`, `CurKb` = `ICurrentStoryKnowledgeBase.Current`.

### 0.3 Global conventions

| Topic | Rule | Source |
|---|---|---|
| Unexpected exception in any handler | `Error · "Error" / ex.Message · 4 s`, then `LogService.WriteToLog(ex.Message)`. Clear any busy flag. Unless a row says otherwise, this is the "Failure result". | V1 (every component) |
| Service progress events | Library and Workspace subscribe to `Svc.TextEvent` and `Orch.ReadTextEvent` and show `Info · "" / e.Message · e.DisplayLength × 1000 ms`. Unsubscribe on dispose. | [Index.razor:437-461](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor) |
| File export | Every download calls `Export.SaveAsync(name, stream, contentType)`. `false` (MAUI save dialog cancelled) shows **no** toast **(new)**. The browser always returns `true`. | Plan D19 |
| File pick | `InputFile` in both apps, with `accept` set. Over the size cap: `Error · "Error" / "The file is larger than {N} MB." · 4 s` **(new)**, nothing is read. | Plan D19, §9 |
| Model dropdowns | Every AI model dropdown lists `AppSettings.VerifiedModels` only. Default selection is `AppSettings.Model`, else the first item. With an empty list the dropdown is disabled and shows "No verified models. Verify a model in Settings." **(new)** with a link to `/settings`, and the action that needs a model is disabled. | Plan §5.6, D9 |
| Speech-to-text | `RadzenSpeechToTextButton`, rendered only when `Host.SupportsSpeechToText`. Captured text is **appended** to the bound field. | Plan §5.6, R9 |
| Dialogs | Radzen `DialogService.OpenAsync`, `CloseDialogOnEsc = true`, `ShowClose = true`, except while a dialog is busy, when Esc and the close button are disabled **(new)**. Widths come from V1 where V1 has the dialog. Every dialog has a 16 px radius, a layered shadow around its edges and a lighter mask **(new, Plan Q16)**. | V1, KGB |
| Busy | V1 pattern: an indeterminate `RadzenProgressBar` replaces the dialog or panel content while a service call runs. Buttons that start another call are disabled while busy **(new)**. | V1 |
| Delete | Every Delete asks for confirmation. | Plan §5.6 |
| Renames that cascade | Ask first, then run the cascading V1 method, with the busy bar. | V1, Plan §5.6 |
| Prose rendering | Paragraph text renders as plain text (`white-space: pre-wrap`), never as `MarkupString`. Indenting is CSS (`text-indent`), not `&nbsp;`. **(new)**, replaces V1's `MarkupString` + `AddParagraphIndenting` (imported manuscripts are untrusted, Plan §9). | Plan §9 |

### 0.4 Routes and query parameters

| Route | Page | Source |
|---|---|---|
| `/` | Library | D20 |
| `/logs`, `/settings` | Logs, Settings (`/utility` was removed with fine-tuning, D23) | D20 |
| `/help` | Help: the user guide (§7a) **(new, Plan Q18)** | — |
| `/story/{title}?tab=<tab>&p=<kbPath>` | Story Workspace. `title` is `Uri.EscapeDataString`. | D20, D29 |

`tab` values, in tab order (§9.4): `details`, `timelines`, `locations`, `characters`, `chapters`, `kb` (D13) and `chat`. The `tab` query parameter itself is **(new)**. A missing or unknown `tab` means `chapters` (Plan §5.6). Changing tabs updates the URL with `replace: true` **(new)**. `p` is read only when `tab=kb`. `&create=1` (from `KbLinkBuilder.CreateHref`) creates the page instead of opening it (§17.5).

---

## 1. App shell (`MainLayout`)

Layout: [Library.dc.html](Design/Library.dc.html) top bar. Logo, wordmark "AI StoryBuilders" with the subline "Manuscript studio" **(design)**, then the menu.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Get a Developmental Edit Report **(new, Plan Q24)** | purple pill link, left of Home · — | always | open `https://Pro.AIStoryBuilders.com` outside the app | — | — | — |
| Home | `RadzenMenuItem` · `home` | always | navigate `/` | — | Item shows the selected state | — |
| Logs | menu · `assignment` | always | navigate `/logs` | — | selected state | — |
| Settings | menu · `line_style` | always | navigate `/settings` | — | selected state | — |
| Help **(new, Plan Q18)** | menu · `help_outline` | always | navigate `/help` | — | selected state | — |

- Icons are V1's ([Index.razor:20-32](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor)).
- V1 hides Home, Logs and Utility until a key is set. V2 always shows Home, Logs, Settings and Help (Plan §5.6, Q18); V1's Utility is removed (D23). **First run:** when `Settings.GetAsync()` returns `ApiKeyConfigured == false` for a provider that needs a key, the first navigation to `/` redirects to `/settings` once per app start (Plan §5.4 "The first run starts at Settings").
- The menu and the pill are hidden inside `/story/*`, which has its own top bar ([StoryWorkspace.dc.html](Design/StoryWorkspace.dc.html)).
- **Update check, browser app only (V1 Online, Plan Q24):** once per app start, `GET https://aistorybuilders.com/api/VersionOnline/getversiononline?paramGUID={guid}&paramCurrentVersion={version}`. If the published version is newer, a strip under the top bar links "* Update Available - Close web browser and reload *" to the documentation site. A failed check shows nothing. The Windows app makes no check.

---

## 2. Library (`/`)

Layout: [Library.dc.html](Design/Library.dc.html), Home view. V1: [Index.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor).

Header: "Your stories" **(design)**, sub "Start a new manuscript, import a project, or open one you're working on." **(design)**.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New Story | Success · `add` | not busy | Open the New Story dialog (§3) | — | §3 | §3 |
| Import Story | Light · `publish` | not busy | `InputFile accept=".stybld"` (§4) | — | §4 | §4 |
| Import docx / pdf / txt **(design)** | Info · `description` | not busy | Open the Import Manuscript dialog (§5) | — | §5 | §5 |
| Story card **Select** | Light · — | not busy | WASM: `await Persist.OpenAsync(title)` first. Then navigate `/story/{EscapeDataString(title)}`. | — | The Workspace opens on the Chapters tab | WASM, zip missing or corrupt: the card shows "Story data is missing from browser storage" **(plan, §7.1)** and a **Remove from library** button. Quota while flushing another story: the quota banner text from §9 as an Error toast, and the selected story does not open (Plan §5.5). |
| Remove from library | Danger · `delete` | only on a card in the missing-data state (WASM) | `Svc.DeleteStoryAsync(title)` | **Remove from library** / "Are you sure?" **(new)** | Reload the list | Global |
| No-model card: Open Settings **(new)** | Primary · `line_style` | shown when `AppSettings.VerifiedModels` is empty | navigate `/settings` | — | — | — |

| Item | Spec |
|---|---|
| Data | `await Svc.GetStorysAsync()` on init and after every action that returns to the Library |
| Card | A book cover colored by the story's Style, with the title's first letter **(new, Plan Q16)**; the title (Newsreader, up to two lines); a Style chip (`"Story"` when empty) and `"Edited {relative time}"` when `StoryIndexEntry.SavedAt` is set **(new)**; and **Select** (Light, `arrow_forward` on the right). Clicking anywhere on the card does the same as **Select** **(new)**. A count line "{n} stories" sits above the grid **(new)**. The design's chapter count is left out, because in WASM it would mean opening every zip (§19). |
| No-model card **(new)** | Shown above the cards when there is no verified model. Text: "Connect an AI provider and verify a model before you create or write stories." **(new)**. New Story stays enabled, but its model dropdown is empty and Create Story is disabled (§0.3). |
| Empty state **(new)** | "No stories yet. Create one with New Story, or import a project.", in a dashed panel with an `auto_stories` icon |
| Loading **(new)** | Four skeleton cards until `GetStorysAsync` returns |
| Busy (creating or importing) | V1: an indeterminate `RadzenProgressBar` at the top. During manuscript import, progress shows inside the Import Manuscript dialog (§5). The action buttons are disabled **(new)**. |
| Keyboard | none |

---

## 3. New Story dialog

V1: [NewStory.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Story/NewStory.razor) → [StoryEdit.razor:13-39](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Story/StoryEdit.razor). Layout: Library artboard "NEW STORY dialog". Dialog title "New Story", eyebrow "Create" **(design)**, width 650 px.

| Field | Control | Limits | Default | Validation (on Create Story) |
|---|---|---|---|---|
| "Story Title:" | `RadzenTextBox`, placeholder "Up to 20 characters" **(design)** | `MaxLength=20` (Plan F2) | empty | Empty: `Error · "Error" / "Story Title is required" · 4 s`. Then `StoryTitle.Validate(title)` (D29); on error: `Error · "Error" / <validator message> · 4 s` (messages in the table below). Then `TrimInnerSpaces`. Duplicate (case-insensitive, through `GetStorysAsync`): `Error · "Error" / "Story name already exists" · 4 s`. |
| Speech-to-text | `RadzenSpeechToTextButton` | — | — | Appends to **Synopsis** (V1 does this although the button sits under the title) |
| "Short Story Synopsis:" | `RadzenTextArea Rows=6`, placeholder "Describe the story in a few sentences..." **(design)** | `MaxLength=1000` | empty | none (V1) |
| "Chapters:" | `RadzenDropDown` `1, 2, 3` | — | `3` | — |
| "AI Model:" | `RadzenDropDown` of verified models | — | the default model | Required; Create Story is disabled without one |

`StoryTitle.Validate` messages **(new)**. Each is shown as the toast detail.

| Rule (D29) | Message |
|---|---|
| Over 100 characters | "Story Title must be 100 characters or fewer." |
| Contains `/ \ : * ? " < > \| #` | "Story Title cannot contain / \ : * ? \" < > \| or #" |
| Contains `..` | "Story Title cannot contain '..'." |
| Leading or trailing space or dot | "Story Title cannot start or end with a space or a dot." |
| Windows reserved name | "'{title}' is a reserved name. Choose another title." |

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Create Story | Success · `add` | a model is selected | Validate, then close the dialog with the story. The Library shows the busy bar. Set `story.Theme` to V1's default system message (quote it exactly from [Index.razor:169](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor)). Then `await Svc.AddStory(story, modelId)`, then `await Svc.EnsureGraphExistsAsync(title)`. | — | Busy off. Navigate `/story/{title}?tab=chapters` (V1 opened the story right away). Progress toasts come from `TextEvent` ("Create the Character files", "Create the Location files", "Create the Timeline file", "Create Chapter {N}") and `ReadTextEvent` ("Calling AI provider..."). "Calling AI provider..." closes when `ReadTextEnded` says the call is over, whether it succeeded or failed; its 70-second display time is only a fallback. | Validation: the dialog stays open with the toast above. Service exception: Global; the Library returns to normal. |
| Close (X) | icon button · `close` | not busy | Close, return nothing | — | — | — |

Keyboard: Esc closes.

---

## 4. Import Story (`.stybld`)

V1: [Index.razor:214-284](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor). Plan §7.7, D6, D22. There is no dialog; the button opens the file picker.

| Step | Behavior |
|---|---|
| Pick | `InputFile accept=".stybld"`, `OpenReadStream(StoryStorageConstants.MaxStybldImportBytes)` (50 MB) |
| Run | Library busy bar. Keep the title list from before. `var msg = await Svc.ImportProjectAsync(bytes)`. |
| Result toast | V1 shows every message as `Info · "Import Result" / msg · 4 s`. V2 keeps the summary and detail, but uses **Success** when the story list gained a title and **Error** otherwise **(new)**. The messages come from the service unchanged (Plan §6.2): "The project '{Title}' was successfully imported.", "The version of the file you are trying to import is not compatible with this version of AI Story Builders. Please update AI Story Builders to the latest version and try again.", "The story {Title} already exists. Backup and delete it before trying to import a new version.", or the exception message. |
| Graph | For the **new** title (the difference between the lists before and after, not V1's `LastOrDefault()`): `await Svc.EnsureGraphExistsAsync(title)` |
| After | Reload the cards. Stay on the Library (V1). |
| WASM quota (§7.7) | The partial story is removed by the service. `Error · "Browser storage is full" (KGB) / "The story was not imported. Delete stories you don't need, then try again." (new) · sticky` |
| Zip Slip or bad entry | The service rejects the whole file; show its message in the Error toast |

---

## 5. Import Manuscript dialog (`ImportManuscriptDialog`)

V1: [Index.razor:286-362](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor). V1 has no dialog: a picker, then a progress bar on the Library. V2 puts both in a small dialog **(new)** titled "Import docx / pdf / txt" **(design)**, width 520 px.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Choose file **(new)** | `InputFile accept=".docx,.pdf,.txt,.md"` | not busy | Read up to 50 MB (`MaxManuscriptImportBytes`). Start right away. Progress starts at 0 with "Starting import..." | — | — | Over the cap: §0.3 |
| (running) | `RadzenProgressBar Value=progress Max=100 ShowValue=true`, then the status text | — | `ManuscriptParsingService.ParseManuscriptAsync(stream, fileName, progress, status)`, then `Svc.PersistImportedStoryAsync(story, Orch)` (V2 builds the graph inside it) | — | Close the dialog and navigate `/story/{title}?tab=chapters` (V1 opened the story) | `Error · "Import Error" / ex.Message · 4 s`. Log `"ImportManuscript: " + ex.Message`. The dialog stays open with the picker reset. A duplicate title throws "A story named '{Title}' already exists. Rename or delete it first." |
| Cancel **(new)** | Light · — | not busy | Close | — | — | — |

| Item | Spec |
|---|---|
| Status texts | From the service, unchanged: "Preparing working directory…", "Extracting text from file…", "Identifying chapter boundaries…", "Parsing chapters — chunk {i} of {n}…", "Creating paragraph structures…", "Chapter {i}/{n}: Generating summary…", "…: Extracting narrative beats…", "…: Identifying characters…", "…: Identifying locations…", "…: Identifying timelines…", "Associating paragraphs with entities…", "Assembling story and generating embeddings…", "Assembling chapter {i}/{n}…", "Import complete!". `TextEvent` toasts also fire ("Writing timelines…" and so on). |
| Indeterminate | When progress is 0, show the indeterminate bar (V1) |
| Busy | Esc, X and Cancel are disabled |

---

## 6. Settings (`/settings`)

Layout: Library artboard Settings view. Header "Settings" **(design)**, sub "Connect an AI provider. Embeddings run locally — no cloud embedding calls needed." **(design)**. The form is KGB's [LlmSettingsForm.razor](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Components/Settings/LlmSettingsForm.razor) (Plan §5.6, D7), with the probe (Plan §5.9, §7.3). V1: [Settings.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Settings.razor).

### 6.1 Fields

| Field | Control | Shown when | Default | Validation (inline, under the field, `kgb-err` style) |
|---|---|---|---|---|
| "AI Provider:" | `RadzenDropDown<LlmProvider>`: OpenAI, Azure OpenAI, Anthropic, Google AI, Ollama (Fake in DEBUG) | always | saved, else Anthropic (Plan §6.3) | — |
| "API Key:" | `RadzenPassword`, placeholder "Paste the key" **(KGB)**. When a key is saved: badge "Configured" **(KGB)** + **Replace** + **Clear key**. When none: badge "Not set" **(KGB)**. | provider needs a key (`LlmDefaults.RequiresKey`) | empty | Missing on Save: "Enter an API key." **(new)**. OpenAI key that does not start with `sk-`: "Invalid API Key - must start with: sk-" (V1 toast text, shown inline) |
| Endpoint | `RadzenTextBox`, placeholder from KGB (`LlmSettingsForm.razor:58`). Label "Azure OpenAI Endpoint:" for Azure, "Endpoint" **(KGB)** for Ollama. | Azure OpenAI, Ollama (`LlmDefaults.RequiresEndpoint`) | empty | Missing: "Enter the endpoint URL." **(new)**. Not an absolute http(s) URL: "Enter a full URL that starts with https:// or http://." **(new)** |
| Model label | V1 labels: "Azure OpenAI Model Deployment Name:", "Anthropic Model:", "Google AI Model:", otherwise "Default AI Model:" | always | the saved model, or `LlmDefaults.DefaultModel(provider)` | Empty: "Pick or type a model name." **(new)** |
| Model | KGB: a filterable dropdown when the list loaded, otherwise a text box. The link "Type a name instead" / "Pick from the list" **(KGB)** switches. Help line: "{n} models available." / "{n} deployments available." / "Loading models…" / "{status} Type the name instead." **(KGB)** | always | — | — |
| Ollama CORS note | Help text **(KGB)**: "The Ollama host must allow this page's origin: set OLLAMA_ORIGINS on the machine running Ollama." | Ollama and `Host.IsBrowser` | — | — |
| Key storage note | KGB help text, browser version or MAUI version, quoted exactly from `LlmSettingsForm.razor:86-93` (it names `LlmDefaults.HostFor`) | key field shown | — | — |
| Embeddings note | "Embeddings are handled locally (no cloud embedding calls needed)." (V1, small text) | always | — | — |
| Privacy note | KGB callout **(KGB)**: "Page text the assistant reads is sent to the selected provider. Ollama keeps everything on the machine that runs it." | always | — | — |

Removed: V1's Azure **Api Version** (Plan D8) and Organization (D31).

### 6.2 Controls

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Get an API key | Secondary · — . Text per provider, from V1: "An OpenAI API Key is required (Click here to get one)", "An Azure OpenAI API Key is required (Click here to get one)", "An Anthropic API Key is required (Click here to get one)", "A Google AI API Key is required (Click here to get one)" | shown when the provider needs a key and none is saved or typed (Plan §5.6, Q9) | `JSRuntime.InvokeVoidAsync("open", "<provider console URL>")` (a constant per provider; MAUI opens the system browser) | — | — | — |
| Refresh models | Light (KGB: Text/Base) · `refresh`, title "Refresh models from API" (V1), `IsBusy` while loading | not loading | `Settings.ListModelsAsync(provider, endpoint, newKey, refresh: true)`. Typing the key or endpoint reloads it after 750 ms (KGB). | — | `Success · "Models Refreshed" / "Found {n} available models." · 3 s` (V1) | No key typed or saved: `Warning · "API Key Required" / "Enter an API key first to fetch available models." · 3 s` (V1). Error: `Error · "Refresh Failed" / "Could not fetch models: {message}" · 4 s` (V1), and the list empties so the text box shows. |
| Replace | Light (KGB: Outlined Base) · — | key configured | Shows the key input; **Cancel** **(KGB)** hides it again | — | — | — |
| Clear key | Danger (Text) · — | key configured | `Settings.ClearApiKeyAsync()` | — (KGB has none) | `Info · "API key cleared" · 3 s` **(KGB)**. Reload the model list. | Global |
| Test connection | Light (KGB: Outlined Base) · `network_check`, `BusyText="Testing…"` **(KGB)** | not busy | If dirty, run **Save** first and stop on failure. Then `Settings.TestConnectionAsync(30 s)`. | — | Callout (success): "Connected to {Provider} ({model})." **(KGB)** | Callout (danger) with the returned message |
| Save | Primary · `save`, `IsBusy`, `BusyText="Verifying model…"` **(new)** | dirty (KGB `Dirty`) and inline validation passes | `var r = await Settings.SaveAsync(provider, model, endpoint, newKey, ct)`. The service probes when `model` is not verified for this provider and host (Plan §5.9): Connect, then A (tool call), then B (graph parse). | — | `Success · "Settings saved" · 3 s` **(KGB)**. If a probe ran, also `Success · "Model verified" / "{model} passed the capability check." · 4 s` **(new)**. Clear the key box, reload the model list, raise `Changed` so other dropdowns refresh. | `r.Saved == false` with a probe: danger callout "{model} did not pass the {stage} check: {r.Probe.Message} Nothing was saved." **(new)**. Stage names **(new)**: Connect = "connection", ToolCall = "tool call", GraphParse = "knowledge graph". Other error: callout with `r.Error`. WASM quota: callout "Browser storage is full, so the settings weren't saved. Export stories you don't need with Export Project, then delete them from the Library." **(new; KGB points to its own backup)** |

Behavior notes:

- Changing the provider sets the model to the saved one (if it is the saved provider) or `LlmDefaults.DefaultModel` and reloads the list (KGB `OnProviderChanged`).
- Changing provider or endpoint host filters the verified list on Save; changing only the key keeps it (Plan §5.9).
- After the first successful Save, a user who arrived through the first-run redirect is sent to `/` **(new)**, as V1's `SettingsChanged` returned to Home.

### 6.3 Verified models panel (`VerifiedModelsPanel`, new in V2)

Plan §5.6, §5.9, D9, D23. Title "Verified models" **(plan)**. Sub "Only these models can be used for writing and chat. A model is added after it calls a tool and reads a small knowledge graph correctly." **(new)**.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Row: model name (mono), "Verified {date}" **(new)**, badge "Default" **(new)** on the default model | — | — | — | — | — | — |
| Remove | Danger (Text) · `delete` | not the default model. The default row's button is disabled with the tooltip "This is the default model. Pick another default first." **(new)** | `Settings.RemoveVerifiedModelAsync(model)` | **Remove model** / "Remove {model} from the verified models? It will have to pass the check again before it can be used." **(new)** | Row removed; `Changed` refreshes the dropdowns | Global |
| Model to add **(new)** | Filterable dropdown over the provider's model list, or a text box (same switch as §6.1) | settings saved (key and endpoint present) | — | — | — | — |
| Add model | Success · `add`, `BusyText="Verifying…"` **(new)** | a name is entered, it is not already verified, nothing is busy | `Settings.VerifyModelAsync(model)` (uses the saved provider, endpoint and key) | — | Row added. `Success · "Model verified" / "{model} passed the capability check." · 4 s` **(new)** | Danger callout under the panel: "{model} did not pass the {stage} check: {message}" **(new)** |

| State | Text |
|---|---|
| Empty **(new)** | "No verified models yet. Save your settings, or add a model, to run the capability check." |
| Loading | Skeleton rows (KGB `kgb-sk`) |

The list is `AppSettings.VerifiedModels` (already filtered to this provider and host, Plan §6.3).

### 6.4 Storage usage panel (WASM only, `StorageUsagePanel`)

Plan §5.5 step 7.

| Item | Spec |
|---|---|
| Meter | `RadzenProgressBar Value=EstimatedBytes Max=AssumedQuotaBytes`. Label "Browser storage: {used} of about 5 MB ({pct}%) · {n} stories" **(new)** |
| Warning at 80% or more | Warning callout: "Browser storage is almost full. Export the stories you want to keep, and delete the ones you don't need." **(new)** |
| Hidden | When `Persist.GetUsageAsync()` returns null (MAUI) |
| Embedding self-test (DEBUG only) | Light · `science`, text "Embedding self-test" **(plan, §10)**. Embeds the 5 fixture sentences and lists the cosine against `Fixtures/embeddings.json`. A pass is ≥ 0.98. |

Keyboard (Settings page): Enter in a text field does not submit **(new)**.

---

## 7. Logs (`/logs`)

V1: [Logs.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Logs.razor). Layout: Library artboard Logs view. Header "Logs" **(design)**, sub "Activity and error log for this installation." **(design)**.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Export Log | Primary · `save_alt` | log not empty | Build V1's HTML table (one `<tr><td>` per line), **HTML-encoding each line** **(new)**. `Export.SaveAsync("AIStoryBuildersLog_{yyyy-MM-dd_HH-mm-ss}.html", …, "text/html")`. | — | `Success · "Saved" / "The file was saved successfully." · 4 s`. V1's variant names the file path, which V2's `IFileExportService` does not return. | Global |
| Clear Log | Danger · `delete` | always | `IAppDataStore.WriteAsync(AppDataFile.Log, "Log cleared on " + DateTime.Now)` (V1 text), then reload | **Clear Log** / "Are you sure?" **(new)** (V1 clears without asking) | `Success · "Success" / "Log Cleared" · 4 s` | Global. WASM quota: Error toast summary "Browser storage is full" **(KGB)** |

| Item | Spec |
|---|---|
| Data | `IAppDataStore.ReadAsync(AppDataFile.Log)`, split on `\n`, drop a trailing empty line. Newest first, at most 1,001 lines (V1 format). |
| Grid | `RadzenDataGrid`, columns **Date** and **Entry** **(design)**, paging 20 per page **(new)**, `ShowPagingSummary`. |
| Date column **(new)** | V1 lines have no date. V2's `LogService.WriteToLog` writes `"{yyyy-MM-dd HH:mm:ss} {text}"` (invariant culture, still one text line, so V1 can read it). The grid shows the leading timestamp in **Date** when a line matches `^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} `, and "—" otherwise. |
| Empty **(new)** | "The log is empty." |
| Loading | Indeterminate bar |

---

## 7a. Help (`/help`) **(new, Plan Q18)**

V1 has no Help page; it links to its GitHub wiki. V2 shows [User-Guide.md](User-Guide.md) inside the app, so the page and the guide never drift apart.

| Item | Spec |
|---|---|
| Content | `Docs/User-Guide.md` is embedded in the Shared library (`Help/User-Guide.md`) and rendered by `HelpMarkdown` with Markdig: pipe tables, emphasis extras, GitHub-style heading ids. Raw HTML is disabled. |
| Contents list | A sticky list of the guide's level-2 headings, left of the guide. Hidden below 900 px. |
| Anchors | Heading ids match GitHub's (`help#1-connect-an-ai-provider`). In-page links `#id` are written as `help#id`, because `<base href="/">` would send a bare `#id` to the library. |
| Images | Only `images/user-guide/<file>` images are shown, from `_content/AIStoryBuildersV2.Shared/images/user-guide/<file>`, lazy-loaded. The Shared project links `Docs/images/user-guide/*.png` in as static assets with `Docs/` as their content root, so there is one copy. Other images show their alt text. |
| Diagram | No Mermaid script ships (D18). A `mermaid` flowchart is shown as one row per arrow: "*from* → *to*". |
| Links | `http`/`https`/`mailto` links open in a new tab; any other link shows its text only. |

Keep the guide's headings stable: the Help contents list and any `help#…` link depend on them. Change the guide, not the page.

---

## 8. Utility (removed)

V1's Utility page (Fine Tuning, Create Training Data, JsonL Utility, Edit Model Name) is **removed** in V2: fine-tuning is no longer supported (Plan D23, superseded 2026-11-05). There is no `/utility` route and no menu item.

---

## 9. Story Workspace shell (`/story/{title}`)

Layout: [StoryWorkspace.dc.html](Design/StoryWorkspace.dc.html) top bar and tab strip. V1: [StoryControl.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Story/StoryControl.razor). Plan §5.5, §5.6, §5.7, D13, D20.

### 9.1 Open and close

| Step | Behavior |
|---|---|
| Enter | `Persist.OpenAsync(title)` (WASM extracts the zip; MAUI no-op). Then `var kb = await IStoryKnowledgeBaseProvider.OpenAsync(title)` and set `ICurrentStoryKnowledgeBase.Current = kb` (D13). Load the story with `GetStorysAsync()` and match the title, ignoring case. Subscribe to `Persist.Changed`, `Svc.TextEvent` and `Orch.ReadTextEvent`. |
| Unknown title | Full-page message "This story doesn't exist." **(new)** and a **Back to library** button (Primary, `arrow_back`) |
| Loading | Indeterminate bar under the tab strip until the story loads |
| Leave | `NavigationLock` on any navigation away from `/story/{title}`: (1) if a KB page is dirty, KGB `AskUnsavedAsync` (§17.5); (2) `await Persist.FlushAsync(title)`; (3) on failure, see Close workspace; (4) set `CurKb = null`, `await IStoryKnowledgeBaseProvider.CloseAsync(title)`, `IStoryChatService.ClearSession(title)` **(new)**. |
| MAUI window close | `WindowsShell` flushes and confirms when the story is dirty or a KB page has unsaved edits (Plan §8) |

### 9.2 Top bar controls

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Title | Story title, subline "Story workspace · AIStoryBuilders" **(design)** | — | — | — | — | — |
| Save state | Pill with icon: `check_circle` "Saved", `sync` "Saving…", `edit` "Unsaved", `error` "Save failed" **(plan, §5.6)**. "Save failed" adds **Retry** (Light, small). MAUI always shows "Saved". | — | Driven by `Persist.Changed` and `Persist.IsDirty(title)` | — | — | — |
| Retry (in the pill) | Light · `refresh` | Save failed | `Persist.FlushAsync(title)` | — | Pill shows "Saved"; the quota banner closes | The banner stays |
| Close workspace | Icon button · `close`, aria-label "Close workspace" **(design)** | always | Navigate `/` (runs §9.1 Leave) | Only if the flush fails: **Unsaved changes** / "Your latest changes could not be saved to browser storage. They are kept only until this browser tab closes. Leave anyway?" **(new)**, with Yes / No | Library | No: stay; the banner stays |

### 9.3 Banners (WASM)

| Banner | Text | Buttons | Action |
|---|---|---|---|
| Quota (Plan §5.5 step 5) | "Browser storage is full. Your latest changes are kept in this tab but are not saved." **(new)** | **Export Project** (Primary, `save_alt`), **Retry** (Light, `refresh`) | Export Project: same as Details §10.2 (built from MEMFS). Retry: `Persist.FlushAsync(title)`. |
| Changed in another tab (Plan §5.5 step 6) | "This story was changed in another tab" **(plan)** | **Reload** (Primary, `sync`), **Keep mine** (Light) | Reload: `Persist.ReloadAsync(title)` (drops the MEMFS copy and the dirty flag), then reload the current tab. Keep mine: `Persist.MarkDirty(title)` then `FlushAsync`. |

### 9.4 Tab strip

Tabs, left to right: "Details", "Timelines", "Locations", "Characters", "Chapters", "Knowledgebase" **(design)** and "Chat" (V1). **Chat is the last tab**, to the right of Knowledgebase (plan Q19). Default: Chapters (Plan §5.6). Built as a tab strip (`<nav class="asb-tabs" role="tablist">` with one `<button role="tab">` per tab, `aria-selected` on the current one), not `RadzenTabs`, so only the current tab's body is rendered.

| On activating | Call (V1 `OnTabChange`) |
|---|---|
| Timelines | `Svc.GetTimelines(story)` |
| Locations | `Svc.GetLocations(story)` |
| Characters | `Svc.GetCharacters(story)` |
| Chapters | `ManuscriptEditor.LoadChapters(story, null)` (keeps the current chapter if it still exists, **(new)**) |
| Chat | `Svc.EnsureGraphExistsAsync(title)`; rebuilds when `StoryGraphCache` marks the story dirty (D17) |
| Knowledgebase | none; the KB components react to `CurKb` |
| Leaving Knowledgebase with a dirty page | KGB `AskUnsavedAsync` first (§17.5). Cancel keeps the KB tab selected. |

---

## 10. Details tab

V1: [StoryEdit.razor:40-111](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Story/StoryEdit.razor). Layout: workspace artboard DETAILS. Plan §5.6 Details row.

### 10.1 Fields

| Field | Control | Limits | Default |
|---|---|---|---|
| "Story Title:" | Read-only text | — | — |
| "Style:" | `RadzenDropDown`: Drama, Sci-Fi, Romance, Comedy, Fantasy, Mystery, Thriller, Children, Young Adult, Action, Crime, Dystopian, Horror (V1 list and order) | — | `Drama` when null |
| "Synopsis:" | `RadzenTextArea Rows=5` | `MaxLength=1000` | — |
| "System Message" (collapsible `RadzenPanel`, collapsed) | `RadzenTextArea Rows=5` bound to `Story.Theme` | `MaxLength=1000` | — |
| "World Facts" (collapsible, collapsed) | `RadzenTextArea Rows=8`, placeholder "Enter persistent world-building facts, one per line (e.g., Magic is illegal in the Northern Kingdom)" | `MaxLength=2000` | — |

No validation on Save (V1).

### 10.2 Buttons

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Save | Success · `save` | not busy | `await Svc.UpdateStoryAsync(story)` | — | `Success · "Story" / "Updated!" · 2 s` | Global |
| Save Word Doc | Primary · `print` | not busy | `Svc.ExportWordDocument(story)` → `Export.SaveAsync("{Title}_{yyyy-MM-dd_HH-mm-ss}.docx", …)` | — | `Success · "Saved" / "The file was saved successfully." · 4 s` | Global |
| Export Project | Primary · `save_alt` | not busy | `Info · "Exporting" / "Story..." · 2 s`, then `Svc.ExportProject(story)` → `Export.SaveAsync("{Title}_{yyyy-MM-dd_HH-mm-ss}.stybld", …)` | — | `Success · "Saved" / "The file was saved successfully." · 4 s` | Global |
| Delete | Danger · `delete` | not busy | `await Svc.DeleteStoryAsync(title)`, then navigate `/` **without** the flush in §9.1 | **Delete Story** / "Are you sure?" | Library. `Success · "Deleted" / "{Title} was deleted." · 4 s` **(new)** | Global |
| Re-load Story | Warning · `sync` | not busy | Open the progress dialog (below), then `await Svc.ReloadStoryAsync(story, progress)` | **Re-load Story** / "This will regenerate all vector embeddings and rebuild the Knowledge Graph. Continue?" | `Success · "Complete" / "All embeddings regenerated and Knowledge Graph rebuilt." · 4 s`. The dialog shows **Close**. | Progress dialog shows a danger alert with ex.Message and **Close**; also the 8 s Error toast (V1); log it |

**Re-load progress dialog** (V1 [ProgressDialog.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/UtilityControls/ProgressDialog.razor), Plan §5.6). Title "Re-load Story", width 440 px, no close button and no Esc while running. Indeterminate bar plus the current message. It starts with "Re-loading Story: Processing all paragraphs, chapters, characters, locations, and Knowledge Graph..." (V1's toast text, used as the first message). Then the `IProgress<string>` messages: "Step 1/5: Saving all story files…", "Step 2/5: Re-embedding paragraphs…", "Step 3/5: Re-embedding chapters, characters, locations…", "Step 4/5: Rebuilding Knowledge Graph…", "Step 5/5: Reloading story data…", "Re-load complete!". V1 also showed each step as a toast; V2 shows them only in the dialog **(new)**. In WASM the first run may load the embedding model; show "Loading the embedding model…" **(new)** until the first embed returns (R12).

Keyboard: none.

---

## 11. Timelines tab and Timeline dialog

V1: [TimelinesControl.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Timelines/TimelinesControl.razor), [TimelinesEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Timelines/TimelinesEdit.razor). Layout: workspace artboard TIMELINES and ENTITY DIALOG.

### 11.1 Tab

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New Timeline | Success · `add` | always | Open the dialog titled "New Timeline" with `Id=-1`, `StartDate=DateTime.Now`, `StopDate=null` | — | Reload the list after close | Global |
| Card **Edit** | Light (small) · — | always | Open the dialog titled "Edit Timeline" | — | Reload the list after close | Global |

| Item | Spec |
|---|---|
| Card | Name (bold), range: `"{start}"` or `"{start} - {stop}"`, each as `ToShortDateString() + ' ' + ToShortTimeString()` (V1) |
| Empty **(new)** | "No timelines yet. Timelines place sections, character backgrounds and location descriptions in time." |

### 11.2 Timeline dialog

Width 650 px. Eyebrow "Story element" **(design)**.

| Field | Control | Limits | Validation (Save) |
|---|---|---|---|
| "Timeline Name:" | `RadzenTextBox` | `MaxLength=200` | Null or empty: `Error · "Error" / "Timeline Name is required" · 4 s`. `\|` characters are removed **(new)** (the name goes into `Timelines.csv`). Another timeline with the same name, ignoring case: `Error · "Validation Error" / "Timeline Name already exists. Choose a new name" · 4 s` **(new)**, modeled on the location message. |
| "Description:" | `RadzenTextArea Rows=2` | `MaxLength=200` | — |
| "Start Date:" **(design)** | `RadzenDatePicker ShowTime=true AllowClear=false` | — | Null: `Error · "Error" / "Start Date is required" · 4 s` |
| "Stop Date:" **(design)** | `RadzenDatePicker ShowTime=true AllowClear=true` | — | Start ≥ Stop: `Error · "Error" / "Start Date must be before Stop Date" · 4 s` |

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Save | Success · `save` | not busy | New: `Svc.AddTimeline(t)`. Renamed: confirm, then busy, then `await Svc.UpdateTimelineAndTimelineNameAsync(t, originalName)`. Otherwise `Svc.UpdateTimeline(t, originalName)`. | Rename only: **Timeline Name Change** / "Are you sure? The entire story will be updated to update the Timeline name." | Close; the tab reloads | Validation toasts above; Global. Confirm No: stay open, nothing saved. |
| Delete | Danger · `delete` | existing timeline (`Id > 0`), not busy | busy, then `await Svc.DeleteTimelineAndTimelineNameAsync(t, originalName)` | **Delete Timeline** / "Are you sure? The entire story will be updated to remove the Timeline." | Close; the tab reloads | Global |
| Close (X) | — | not busy | Close without saving | — | — | — |

---

## 12. Locations tab and Location dialogs

V1: [LocationsControl.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Locations/LocationsControl.razor), [LocationEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Locations/LocationEdit.razor), [LocationDescriptionEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Locations/LocationDescriptionEdit.razor).

### 12.1 Tab

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New Location | Success · `add` | always | Dialog "New Location", `Id=-1`, empty description list | — | Reload after close | Global |
| Card **Edit** | Light (small) · — | always | Dialog "Edit Location" | — | Reload after close | Global |

Card: name, meta = the distinct timeline names of its descriptions, joined with ", " **(new)**, or "No timeline" **(new)**. Empty **(new)**: "No locations yet."

### 12.2 Location dialog

Width 650 px, eyebrow "Story element" **(design)**.

| Field | Control | Limits | Validation (Save) |
|---|---|---|---|
| "Location Name:" | `RadzenTextBox` | `MaxLength=100` | Trimmed, `\|` removed. Empty: `Error · "Error" / "Location Name is required" · 4 s`. New and name exists (`Svc.LocationExists`): `Error · "Validation Error" / "Location Name already exists. Choose a new name" · 4 s` |
| Description list | One sub-card per description: the description text (hidden when it starts with `[`, V1's vector guard), and the timeline name as a heading **(design)**. Each card has **Edit** (Light, small). | — | — |

The list shows for new locations too **(new)**. V1 hides it until the location is saved, although New Description already works.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New Description | Success · `add` | not busy | Open §12.3 with `Id=-1` | — | The entry is added to the in-memory list with `Id = max + 1` | — |
| Sub-card **Edit** | Light (small) · — | not busy | Open §12.3 for that entry | — | In-memory list updated, or the entry removed when the sub-dialog returns `Id=0` | — |
| Save | Success · `save` | not busy | busy. New: `await Svc.AddLocationAsync(l)`. Renamed: confirm, then `Svc.UpdateLocationName(l, original)` and `await Svc.UpdateLocationDescriptions(l)`. Otherwise `await Svc.UpdateLocationDescriptions(l)`. | Rename only: **Location Name Change** / "Are you sure? The entire story will be updated to update the Location." | Close; the tab reloads | Validation toasts; Global. Confirm No: stay open. |
| Delete | Danger · `delete` | `Id > 0`, not busy | busy, then `Svc.DeleteLocation(l)` | **Delete Location** / "Are you sure? The entire story will be updated to remove the Location." | Close; the tab reloads | Global |

### 12.3 Location Description dialog

Title "Edit Location Description" (V1), or "New Location Description" **(new)** when `Id=-1` (Plan §5.6). Width 650 px. Changes stay in memory until the Location dialog's **Save**.

| Field | Control | Limits | Validation |
|---|---|---|---|
| "Location Description:" | `RadzenTextArea Rows=2` | `MaxLength=100` | Empty or whitespace: `Error · "Error" / "Description is required" · 4 s` (V1 checks only for empty, not null) |
| "Timeline:" | `RadzenDropDown`: the story's timeline names plus an empty item | — | — |

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Save | Success · `save` | always | Set `Timeline` to `new Timeline { TimelineName = selected }` or null; close with the entry | — | Back to §12.2 | Validation |
| Delete | Danger · `delete` | `Id > 0` | Set `Id = 0`; close with the entry | **Delete Description** / "Are you sure?" | Entry removed from the list | — |

---

## 13. Characters tab and Character dialogs

V1: [CharactersEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Characters/CharactersEdit.razor), [CharacterEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Characters/CharacterEdit.razor), [CharacterBackgroundEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Characters/CharacterBackgroundEdit.razor).

### 13.1 Tab

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New Character | Success · `add` | always | Dialog "New Character", `Id=-1` | — | Reload after close | Global |
| Card **Edit** | Light (small) · — | always | Dialog "Edit Character" | — | Reload after close | Global |

Card: avatar with the first letter **(design)**, name, meta = the first background description, cut to 60 characters **(new)**, or empty. Empty **(new)**: "No characters yet."

### 13.2 Character dialog

Width 650 px; the body scrolls at 400 px (V1). Eyebrow "Story element" **(design)**.

| Field | Control | Limits | Validation (Save) |
|---|---|---|---|
| "Character Name:" | `RadzenTextBox` | `MaxLength=100` **(new)** (V1 has none) | Empty: `Error · "Error" / "Character Name is required" · 4 s`. Remove `\|`, `[`, `]`. New and the name exists, ignoring case: `Error · "Validation Error" / "Character Name already exists. Choose a new name" · 4 s` **(new)** |
| "Background Type:" | `RadzenDropDown`: Appearance, Goals, History, Aliases, Facts (V1 types, which are written to the file; the artboard's list is mock data) | — | Default `Appearance` |
| Background list | One sub-card per entry, **all types** **(design)**. Heading "{Type}" or "{Type} · {Timeline}" **(design)**, then the description (hidden when it starts with `[`). Each card has **Edit** (Light, small). | — | — |

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Add Background | Secondary · `add` | not busy | Open §13.3 with `Id=-1`, `Type` = the selected Background Type | — | Entry added in memory, `Id = max + 1` | — |
| Sub-card **Edit** | Light (small) · — | not busy | Open §13.3 for that entry | — | Entry updated, or removed when `Id=0` comes back | — |
| Save | Success · `save` | not busy | Validate. If renamed, confirm **first** **(new order)**. Then busy, `await Svc.AddUpdateCharacterAsync(c, originalName ?? c.CharacterName)`, and when renamed `await Svc.UpdateCharacterNameAsync(c, originalName)`. | Rename only: **Character Name Change** / "Are you sure? The entire story will be updated to update the Character Name." | Close; the tab reloads | Confirm No: stay open, nothing saved. Global. |
| Delete | Danger · `delete` | `Id > 0`, not busy | busy, then `Svc.DeleteCharacter(c, originalName)` | **Delete Character** / "Are you sure?" | Close; the tab reloads | Global |

### 13.3 Character Background dialog

Title "Edit Character Background  - {Type}" (V1, two spaces), or "New Character Background - {Type}" **(new)** when `Id=-1`. Width 500 px. In memory until the Character dialog's **Save**.

| Field | Control | Limits | Validation |
|---|---|---|---|
| "Description:" | `RadzenTextArea Rows=2` | `MaxLength=100` | Empty or whitespace: `Error · "Error" / "Description is required" · 4 s` **(new here)**; V1 throws on null. `\|` removed. |
| "Timeline:" | `RadzenDropDown`: timeline names plus an empty item | — | — |

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Save | Success · `save` | always | Set `Timeline` to the matching timeline or null; close with the entry | — | Back to §13.2 | Validation |
| Delete | Danger · `delete` | `Id > 0` | `Id = 0`; close with the entry | **Delete Background** / "Are you sure?" **(new)**; V1 deletes without asking (Plan §5.6) | Entry removed | — |

---

## 14. Manuscript Editor (Chapters tab)

V1: [ChaptersControl.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chapters/ChaptersControl.razor), [ChaptersEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chapters/ChaptersEdit.razor). Layout: [ManuscriptEditor.dc.html](Design/ManuscriptEditor.dc.html) (and the Chapters block of the workspace artboard). Plan §5.6 Manuscript Editor row. The artboard's own top bar ("Saved", **Preview**, Display settings) is **not** built; the workspace top bar replaces it (Q6).

### 14.1 Data and selection

| Item | Spec |
|---|---|
| Load | `Svc.GetChapters(story)`; select the first chapter (or the one returned by New Chapter). Then `GetCharacters`, `GetLocations`, `GetTimelines`, `GetParagraphs(chapter)`. V1 also appends a blank editable paragraph; V2 does not (see **Add section at end**). |
| Default selection | The first section **(design)** |
| `OnlyChapter` | `chapters.Count == 1` (V1). It hides Delete. |
| Chapter display **(new)** | Eyebrow = `ChapterName` ("Chapter 1"). Title = the synopsis's first sentence, cut to 60 characters, or `ChapterName` when the synopsis is empty. A leading `#Beat N -` marker (V1's AI beats) is dropped first, and the title stops at the next marker (Plan Q15). Dropdown text = "{ChapterName}: {title}", or `ChapterName` alone. V1 chapters have no title; see §19. |
| Unsaved synopsis | Switching chapters drops unsaved synopsis edits (V1 behavior) |

### 14.2 Outline (left)

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Header "Chapter outline" **(design)**, badge "Auto-detected" **(design)** | — | — | — | — | — | — |
| "Chapters" dropdown **(design)** | `RadzenDropDown` over the chapters | not busy | Select the chapter, `LoadParagraphs(chapter)`, scroll the paper to the chapter header (smooth) **(design)** | — | — | Global |
| Banner | "This chapter breaks into {n} paragraph(s). Select one to open the paragraph tools." / "No paragraphs detected in this chapter yet. Add one to begin." **(design)** | — | — | — | — | — |
| Chapter node | Text button with the dropdown text | always | Select the chapter header and scroll to it | — | — | — |
| Section node | "Section {n} · {first six words}…" **(design, Plan §5.6)** | always | Select the section, scroll it into view (`block: nearest`) | — | — | — |
| Edit this paragraph | Icon button · `edit`, aria-label "Edit this paragraph" **(design)** | always | Open the paragraph editor (§15) for that section | — | — | — |
| Empty | "No paragraphs yet in this chapter." **(design)** | — | — | — | — | — |

### 14.3 Paper: chapter header and tools

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New Chapter | Success · `add` | not busy | Open the **New Chapter dialog**: title "Chapter {count+1}", field "Synopsis:" (`RadzenTextArea Rows=4 MaxLength=4000`, as V1 `ChaptersEdit`; the paper Synopsis keeps V1's 1000, so both V1 limits stay), **Save** (Success). Save runs `await Svc.AddChapterAsync(chapter, "Chapter{N}")` and returns the name. | — | Reload; select the new chapter (V1 `LoadChapters(story, result)`) | Empty synopsis: `Error · "Error" / "Synopsis is required" · 4 s` (dialog stays open). Global. |
| Save | Success · `save` | not busy | `await Svc.UpdateChapterAsync(selectedChapter)` (saves the synopsis) | — | `Success · "Update" / "Saved" · 2 s` | Global |
| Insert chapter before | Success · `note_add`, icon only, tooltip "Insert chapter before" **(plan)** | not busy | `Svc.RestructureChapters(selected, RestructureType.Add)`; new chapter `ChapterName = "Chapter{selected.Sequence}"`, `Synopsis = "New Chapter"`; `await Svc.InsertChapterAsync(newChapter)`; reload; select "Chapter {N}" | **Add new Chapter before this Chapter** / "Are you sure?" | `Success · "New Chapter" / "Created" · 2 s` | Global |
| Delete | Danger · `delete` | hidden when `OnlyChapter` (V1); not busy | `Svc.DeleteChapter(selected)`, then `Svc.RestructureChapters(selected, RestructureType.Delete)`; reload; select the first chapter | **Delete {ChapterName}?** / "Are you sure?" | List reloads | Global |
| "Synopsis" **(design)** | `RadzenTextArea`, placeholder "Short chapter synopsis..." **(design)** | — | Bound to the chapter; saved only by **Save** | — | — | `MaxLength=1000` (V1 `ChaptersControl`) |

### 14.4 Paper: sections

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Section block | Badge "Section {n}" **(design)**, prose in Newsreader (plain text, §0.3) | always | Click: select it. Double-click: open the editor **(new)**. | — | Selected style | — |
| Edit section | `edit-btn` · `edit`, text "Edit section" **(design)** | the section is selected | Open the paragraph editor (§15) | — | — | — |
| Add first paragraph | Success · `add`, text "Add first paragraph" **(design)** | the chapter has no sections | Open the editor for a new section: `Id=-1`, `Sequence=1`, empty characters, location and timeline | — | — | — |
| Add section at end **(new)** | Light · `add`, text "Add section" **(new)**, under the last section | the chapter has sections | Open the editor for a new section: `Id=-1`, `Sequence=count+1`, inheriting the last section's characters, location and timeline (V1's trailing blank paragraph) | — | — | — |

Empty paper: "This chapter has no paragraphs yet." **(design)**. Loading: indeterminate bar in the paper (V1). Keyboard: none besides Esc in dialogs.

---

## 15. Paragraph editor popup (`ParagraphEditorDialog`)

V1: [ParagraphEdit.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Paragraphs/ParagraphEdit.razor), [ParagraphPrompt.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Paragraphs/ParagraphPrompt.razor), the selectors, and [CharacterAttributes.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Paragraphs/CharacterAttributes.razor). Layout: the artboards' PARAGRAPH EDITOR popup. Plan §5.6 Paragraph editor row, §5.8, §7.5, D15, D16.

One modal (width 760 px **(new)**) with five views. The header shows an eyebrow and a title for each view **(design)**:

| View | Eyebrow | Title | Back arrow ("Back to editor") |
|---|---|---|---|
| edit | "Paragraph editor" | "Section {n}", or "New Section" | no |
| ai | "Generate with AI" | "AI Prompt Instructions" | yes |
| characters | "Paragraph attribute" | "Select Characters" | yes |
| location | "Paragraph attribute" | "Select Location" | yes |
| timeline | "Paragraph attribute" | "Select Timeline" | yes |

The editor works on a **copy** of the paragraph. Nothing is written until **Save**, **Delete** or **Add**.

### 15.1 Edit view (toolbar, left to right)

Text area: `RadzenTextArea Rows=12 MaxLength=5000`, placeholder "Write this paragraph..." **(design)**, content without indenting (V1 `RemoveParagraphIndenting`).

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Speech to text | `RadzenSpeechToTextButton` | `Host.SupportsSpeechToText` (hidden otherwise) | Append to the text | — | — | — |
| AI | Warning (artboard `rz-ai`) · `app_registration`, icon only, aria-label "AI" | not busy | Switch to the ai view | — | — | — |
| Characters | Light · —, text "Characters" | not busy | Switch to the characters view | — | — | — |
| Location | Light · —, text "Location" | not busy | Switch to the location view | — | — | — |
| Timeline | Light · —, text "Timeline" | not busy | Switch to the timeline view | — | — | — |
| Undo | Dark · `undo`, aria-label "Undo" | shown only after a Generate (`Undoable`) | Text = `UndoCache`; `Undoable = false` | — | The note disappears | — |
| Delete section | Danger · `delete`, aria-label "Delete section" **(design)** | existing section (`Id > -1`, V1 `Deleteable`), not busy | `Svc.DeleteParagraph(chapter, paragraph)`; close; reload the chapter | **Delete Section** / "Are you sure?" | The section is gone; selection moves to the previous section **(new)** | Global. No: stay in the edit view. |
| Add section above | Success · `note_add`, aria-label "Add section above" **(design)** | existing section (V1 `Addable`), not busy | `Svc.AddParagraph(chapter, paragraph)`; close; reload | **Add Section above this Section** / "Are you sure?" | The new blank section is selected **(new)** | Global. No: stay. |
| Save ▾ | `RadzenSplitButton` Success · `save`, text "Save"; item "Autodetect Attributes" (value `Autodetect`); toggle aria-label "More save options" **(design)** | not busy | Text empty after trim: `Error · "Error" / "Paragraph content is required" · 4 s` and stop. Otherwise busy. If the item is Autodetect, run §15.6 first. Null timeline or location become empty objects. `await Svc.UpdateParagraph(chapter, paragraph)`. | — | Close; reload the chapter; keep the section selected | Global; busy off; the popup stays open with the text |
| Close editor | Primary · `exit_to_app`, aria-label "Close editor" **(design)** | not busy | Close without saving | Only when the text, characters, location or timeline changed: **Unsaved changes** / "Discard your changes to this section?" **(new)** (V1 drops them silently) | — | — |
| X (header) | icon · `close` | not busy | Same as Close editor | same | — | — |

Undo note (shown with Undo): icon `auto_fix_high` and "AI rewrote this section. Use Undo to restore the previous text." **(design)**.

Busy: an indeterminate bar replaces the toolbar and text area (V1); Esc and X are disabled.

### 15.2 AI view (Generate)

| Field | Control | Limits | Default |
|---|---|---|---|
| Speech to text | `RadzenSpeechToTextButton` (capability-gated) | — | appends to the prompt |
| "Describe paragraph or provide prompt instructions (optional):" | `RadzenTextArea`, placeholder "e.g. continue from Freya's threat, keep it tense" **(design)** | `MaxLength=500` | empty |
| "Number of words to generate:" | `RadzenDropDown`: 10, 20, 50, 100, 200, 300, 500, 800, 1000 | — | V1 `SetNumberOfWords(text)`: fewer than 10 words gives 200; under 20 gives 20; under 50 gives 50; under 100 gives 100; under 200 gives 200; under 300 gives 300; under 500 gives 500; under 800 gives 800; otherwise 1000 |
| "AI Model:" | Verified-model dropdown (§0.3) | — | the default model |
| Search knowledgebase **(plan)** | `RadzenCheckBox` under AI Model | — | D16: checked when `await CurKb.IsEmptyAsync()` is false. When the KB is empty: unchecked and disabled, with the help text "This story's knowledge base is empty" **(plan)**. The choice is kept per story and per surface for this session (D16). |

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Generate **(plan; the artboard and V1 say "Save")** | Success · `auto_awesome` | a model is selected, not busy | Busy with the text "Writing…" **(new)**. `prompt = new AIPrompt { AIPromptText, NumberOfWords, ModelId, SearchKnowledgeBase }`. `before = GetParagraphs(chapter).Where(p => p.Sequence < paragraph.Sequence)`. `chars` = story characters that are in `paragraph.Characters`. `master = await Svc.CreateMasterStory(chapter, paragraph, chars, before, prompt)`. If checked: `ctx = await IKnowledgeContextRetriever.RetrieveAsync(title, query, modelId, ct)` (query = prompt text + chapter synopsis + characters, location and timeline, Plan §5.8), and `master.KnowledgeBaseNotes = ctx.Notes`. `text = await Orch.WriteParagraph(master, prompt, modelId)`. | — | `UndoCache` = old text, `Undoable = true`, text = the result, back to the edit view with the undo note. Not saved yet (V1). | `ctx.Warning` set: `Warning · "Knowledge base" / ctx.Warning · 6 s` **(new summary)**, and generation continues without notes (D15). Model or JSON error: `Error · "Error" / ex.Message · 4 s`; the text is unchanged; back to the edit view (§7.5). Unverified model: the `GetClientAsync` reason, "Model '{m}' has not passed the capability check. Verify it in Settings." **(plan)** |
| Back arrow | icon · `arrow_back` | not busy | Back to the edit view, nothing changed | — | — | — |

### 15.3 Characters view

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Checklist **(design)** | `RadzenCheckBoxList` of all character names, sorted; the paragraph's characters are checked | — | — | — | — | — |
| Cancel **(design)** | Light · — | always | Back to the edit view; selection unchanged | — | — | — |
| Save **(design)** | Success · — | always | `paragraph.Characters` = the checked names (`new Character { CharacterName = n }`, V1); back to the edit view | — | The edit view shows the new selection (in the popup only until Save) | — |

Empty **(new)**: "This story has no characters yet. Add them on the Characters tab."

### 15.4 Location view

| Control | Spec |
|---|---|
| "Location" **(design)** dropdown | Locations sorted by name, plus "(none)" **(design)**, which maps to V1's empty location |
| Cancel | Light; back, unchanged |
| Save | Success; `paragraph.Location` = the matching location from the story list (V1 reloads it so it has its descriptions), or an empty `Location` for "(none)"; back |

### 15.5 Timeline view

| Control | Spec |
|---|---|
| "Timeline" **(design)** dropdown | Timeline names sorted, plus "(none)" **(design)**, which maps to "" |
| Cancel | Light; back, unchanged |
| Save | Success; `paragraph.Timeline.TimelineName` = the choice ("" for none); back |

### 15.6 Autodetect Attributes (Save ▾ item)

V1 [ParagraphEdit.razor:284-472](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Paragraphs/ParagraphEdit.razor), Plan §5.6. Runs inside the Save busy state, before `UpdateParagraph`.

| Step | Behavior |
|---|---|
| 1 | `Success · "Info" / "Calling AI to detect Attributes..." · 2 s` **(new; V1 says "Calling ChatGPT to detect Attributes...", which is wrong for other providers)** |
| 2 | `found = await Orch.DetectCharacters(paragraph)` |
| 3 | New characters (not in the story): `Success · "Info" / "Detecting attributes on new characters…" · 2 s`, then `Orch.DetectCharacterAttributes(paragraph, newOnes, "New Character")` |
| 4 | Existing characters: `Success · "Info" / "Detecting attributes on existing characters…" · 2 s`, then `Svc.SimplifyCharacter(existing, paragraph)` and `Orch.DetectCharacterAttributes(paragraph, simplified, "Existing Character")` |
| 5 | Any results: open the **Select Character Attributes** dialog (V1 title, 500 × 360 px). A `RadzenTree` with checkboxes over `CharacterDisplay`, all expanded, and the button "Add Characters and Attributes" (Primary). Closing with X adds nothing. |
| 6 | For the checked values (`Name\|Action\|Type\|Text`): add new characters to the paragraph and the story list; add attributes to existing characters; `await Svc.AddUpdateCharacterAsync(c, c.CharacterName)` for each character in the paragraph (V1) |
| 7 | For story characters whose name appears in the text but not in the paragraph: add them, with `Success · "Info" / "Adding {name}" · 2 s` |
| Failure | Global; nothing is saved; the popup stays open |

Keyboard (popup): Esc closes (with the unsaved check) when not busy. No other shortcuts.

---

## 16. Chat tab (`StoryChatTab`)

V1: [ChatControl.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chat/ChatControl.razor), [ChatMessageRenderer.razor](../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chat/ChatMessageRenderer.razor). Layout: workspace artboard CHAT. Plan §5.6 Chat row, §5.8, §6.5, §7.4.

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Header "Story Chat Assistant" | `forum` icon | — | — | — | — | — |
| Model dropdown | Verified models (§0.3) | not streaming | Sets the model for the next turns. It does **not** change the saved default (V1 changed it in memory). Kept for the session. | — | — | — |
| Include Knowledgebase **(plan)** | `RadzenCheckBox` next to the model | not streaming; disabled when the KB is empty, with the tooltip "This story's knowledge base is empty" **(plan)** | Sets `StoryChatRequest.SearchKnowledgeBase`. When checked, the chat can read **and propose edits to** every KB page and manage its folders (see "Knowledge base page edits" and "Knowledge base folders" below). When unchecked, it can't read the KB but can still propose deleting a page or folder by path (plan Q21). Default and persistence as in D16. | — | — | — |
| Clear conversation | Icon button · `restart_alt` **(new icon; V1 uses the 🔄 emoji)**, title "Clear conversation" | not streaming, at least one message or proposed change | Clear the bubbles and proposed changes; `IStoryChatService.ClearSession(title)` | — (V1 has none) | Empty state shows | — |
| Input | A plain `<textarea rows="2">` (`asb-chat-composer`, aria-label "Message"), placeholder "Ask about your story..." | disabled while streaming | Enter sends; Shift+Enter adds a newline (V1, Plan §5.6) | — | — | — |
| Send | Primary · —, text "Send" | not streaming, the graph is not loading, input not blank | Add the user bubble, clear the input, stream `SendMessageAsync(new StoryChatRequest(title, text, model, kbChecked))`, appending chunks to the AI bubble | — | The AI bubble is final | Exception: an error bubble with "Error: {message}" (V1) and **Retry** **(plan)** |
| Retry **(plan)** | Light · `refresh`, in the error bubble | not streaming | Remove the error bubble and send the same user text again (no second user bubble) **(new)** | — | — | Same as Send |
| Apply **(new, SEC-001)** | Primary small · `check`, in a proposed-change card | not streaming, no other Apply running | `IStoryChatService.ApplyPendingAsync(title, id)` runs the change exactly as proposed | — (the card is the confirmation) | The card goes; an AI bubble "Applied: {summary}" | An AI bubble "Could not apply the change: {error}"; an unknown id gives "This change is no longer pending." |
| Discard **(new, SEC-001)** | Light small · `close`, in a proposed-change card | not streaming, no Apply running | `DiscardPending(title, id)` | — | The card goes; an AI bubble "Discarded: {summary}" | — |

| Item | Spec |
|---|---|
| Bubbles | User: right, accent background, label "You". AI: left, light, label "AI", rendered with `SafeMarkdown.ForChat` (no images, inert external links; Plan §8). `[[path]]` citations link to `KbLinkBuilder.PageHref(path)` (D13), which switches to the Knowledgebase tab and opens the page. |
| Streaming | A provisional AI bubble showing "Thinking..." until the first chunk |
| Graph loading | On tab activation (§9.4). Send is disabled meanwhile; show "Loading the story graph…" **(new)** under the header. |
| Empty | "No messages yet. Ask a question about your story!" and, smaller, the V1 examples line: "Examples: "Are there any orphaned characters?", "Tell me about the main characters"" |
| Service messages | Shown as AI bubbles exactly as yielded, for example the `GetClientAsync` reasons "Add an API key in Settings" and "Model '<m>' has not passed the capability check. Verify it in Settings." **(plan)** |
| Graph mutations | **Changed from V1 (SEC-001, plan Q17):** the mutation tools have no `confirmed` parameter, so the model can only propose. Each proposal shows after the bubbles as a dashed card, labelled "Proposed change · {tool}", with the preview summary, a collapsed **Details** list of the exact arguments, and **Apply**/**Discard**. Nothing is written until Apply. The model learns what the user chose at the start of the next turn. Cards survive tab switches and are dropped with the conversation (Clear conversation, Close workspace). After an applied mutation the entity tabs reload when next activated (§9.4). |
| Knowledge base page edits **(new, plan Q19)** | With **Include Knowledgebase** checked, the chat can edit **every page in the knowledge base**, in any folder: create a page (`kb_create_page`), replace a page's full Markdown (`kb_update_page`) or delete a page (`kb_delete_page`). Each edit is a proposed-change card like a graph mutation, labelled "Proposed change · kb_update_page" and so on, with a summary such as "Replace the text of the knowledge base page Lore/Necklace.md" and the full new Markdown under **Details**. Nothing is written until Apply. Apply saves through the KB workspace, so the explorer, links, backlinks and graph show the change when the Knowledgebase tab is next opened. If the page was saved in the Knowledgebase tab after the proposal, Apply fails with "Could not apply the change: {path} changed since the change was proposed." and the page is unchanged. A refused proposal (the page already exists, there is no page at that path, or the path leaves the KB) makes no card; the model gets the error and tells the user. Unchecked, the chat cannot read, create or rewrite pages, but it can still propose deleting one (plan Q21). |
| Knowledge base folders **(new, plan Q20)** | With **Include Knowledgebase** checked, the chat can also **manage folders** (unchecked, only folder delete is offered, plan Q21): create a folder (`kb_create_folder`; missing parents are created too), rename one (`kb_rename_folder`), move one into another folder or to the root (`kb_move_folder`), or delete one with all its pages and subfolders (`kb_delete_folder`). Each is a proposed-change card with a summary such as "Rename the knowledge base folder Lore to Artifacts", "Move the knowledge base folder Lore to the root" or "Delete the knowledge base folder Lore, with its 3 pages and 1 subfolders". Nothing changes until Apply. Rename and move update links to the moved pages, as in the Knowledgebase tab, and the "Applied:" bubble says in how many pages. A delete fails with "Could not apply the change: {folder} changed since the change was proposed." if the folder's number of pages or subfolders changed after the proposal. The root can't be changed. A refused proposal (no folder there, the name is taken, or a move into itself) makes no card. The Knowledgebase tab shows the new tree when it is next opened. |
| History | Kept while the workspace is open; cleared on Close workspace (§9.1) |
| Keyboard | Enter sends, Shift+Enter newline |

---

## 17. Knowledgebase tab (`KnowledgeBaseTab`)

Layout: workspace artboard KNOWLEDGEBASE. Code: KGB [ExplorerPane.razor](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Components/Explorer/ExplorerPane.razor), [EditorPane.razor](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Components/Editor/EditorPane.razor), [LinksPanel.razor](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Components/Editor/LinksPanel.razor), [GraphPage.razor](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Pages/GraphPage.razor) (as `KnowledgeGraphView`), [Common/](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Components/Common/DialogExtensions.cs) dialogs, routed as in [WorkspacePage.razor](../../../KnowledgeGraphBuilder/KnowledgeGraphBuilder.Shared/Pages/WorkspacePage.razor). Plan §5.6 Knowledgebase row, §5.7, D11–D13.

Every KGB component reads `CurKb.Workspace`, `CurKb.Graph`, `CurKb.State` and `CurKb.Links` instead of injected singletons (D13). Every link uses `CurKb.Links.PageHref` or `CreateHref`. All KB writes mark the story dirty (§5.7). KGB's chat, tour and help are not ported.

### 17.1 Bar

| Control | Style/icon | Enabled when | Action |
|---|---|---|---|
| Pages | Segmented button · `description`, "Pages" **(design)** | always | Show the Pages view |
| Graph | Segmented button · `hub`, "Graph" **(design)** | always | Show the Graph view (whole graph) |
| Stat | "{pages} pages · {links} links" **(design)**, from `CurKb.Graph.GetStatsAsync()`, refreshed on `Workspace.Changed` | — | — |

The view toggle is component state, not a URL parameter **(new)**. Opening a `?p=` link always shows Pages.

### 17.2 Explorer

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| New page | Text/Base · `note_add`, title "New page" **(KGB)** | always | `PageCommands.CreatePageAsync` in the target folder (the selected folder, or the open page's folder). Name dialog: title "New page", label "Page name" or "Page name in {folder}", button "Create", help "Saved as a .md file." **(KGB)**. New pages get KGB's template (front matter title plus `# Title`). | — | Open the page | "Already exists" / "{path} already exists." Warning 6 s **(KGB)**; other errors via `ShowError` (§17.7) |
| New folder | Text/Base · `create_new_folder`, title "New folder" **(KGB)** | always | Name dialog: "New folder", "Folder name" or "Folder name in {parent}", "Create" **(KGB)**; `Workspace.CreateFolderAsync` | — | Parent expands | `ShowError` |
| Refresh | Text/Base · `refresh` **(KGB)** | always | Reload the loaded folders; rerun the search | — | — | — |
| Collapse all | Text/Base · `unfold_less` **(KGB)** | always | `State.ExpandedFolders.Clear()` | — | — | — |
| Search | `RadzenTextBox`, placeholder "Search pages" **(KGB)** | always | 200 ms debounce; `Graph.SearchAsync(q, 25)`; Esc clears | — | Results: "{n} results · Esc clears" **(KGB)**, title, path, snippet with `<mark>` | "No pages match “{q}”." **(KGB)** |
| Tree | `RadzenTree`: folders toggle on click; pages open. Icons `folder`, `folder_open`, `description`. Unsaved pages show a dot titled "Unsaved changes" **(KGB)**. | always | Open = navigate `CurKb.Links.PageHref(path)` | Unsaved current page: §17.5 | — | — |
| Context menu | "Open", "New page here", "New folder here", "Rename (F2)", "Move to…", "Delete (Del)" **(KGB)** | right-click a node | As the rows below | | | |
| Rename | (menu or F2) | a node is selected | If affected pages are dirty: **Save first?** / "Some affected pages have unsaved changes. Save them before continuing?" with Cancel / Save first **(KGB)**. Then a name dialog "Rename page" or "Rename folder", label "New name", button "Rename", help "Links to it are updated in other pages." **(KGB)**. Then `Workspace.RenameAsync`. | (the save-first prompt) | `Success · "Renamed. {n} pages updated." · 4 s` ("1 page updated") **(KGB)**; if open, navigate to the new path | Failed link updates: dialog "Renamed with warnings" / "Renamed to {path}. {n} pages updated, but links in these pages couldn't be updated:" plus the list, OK **(KGB)**. Errors via `ShowError`. |
| Move to… | (menu, or drag and drop) | a node is selected | Save-first prompt, then the **Move {name}** dialog: "Move {path} to:", a folder tree with "(Root)", Cancel / Move **(KGB)**. Move is disabled with "It's already in this folder." or "A folder can't be moved into itself or one of its subfolders." **(KGB)** | (save first) | "Moved. {n} pages updated." **(KGB)** | Drag onto an invalid target: `Warning · "Can't move here" / <reason> · 5 s` **(KGB)**. Root drop zone text: "Drop here to move to the root" **(KGB)** |
| Delete | (menu or Del) | a node is selected | `ConfirmDeleteAsync`, then `Workspace.DeleteAsync` | **Delete {name}?** (KGB). MAUI: "{what} will be moved to the Recycle Bin." with Cancel / Delete. WASM: "{what} will be deleted permanently. This can't be undone." with Cancel / **Export first** / Delete permanently; a folder needs "Type {name} to confirm" **(KGB)**. **Export first** runs Export Project (§10.2) **(new: KGB wires it to its backup service, which V2 does not have)**. | `Success · "Deleted {name}." · 4 s` **(KGB)**; close the editor if it showed that page | `ShowError` |

| State | Text |
|---|---|
| Loading | Skeleton rows (KGB) |
| Empty KB | "The knowledge root is empty." **(KGB)** |

### 17.3 Editor

| Control | Style/icon | Enabled when | Action | Confirm | Success | Failure |
|---|---|---|---|---|---|---|
| Title and breadcrumb | `h1` = front-matter title or the default title; crumb = path joined with " › " **(KGB)**. Unsaved dot. Badge "Invalid YAML" with `warning` when the YAML does not parse **(KGB)**. | — | — | — | — | — |
| Design / Source / Preview | `RadzenSelectBar`, icons `edit_note`, `code`, `visibility` **(KGB)** | always | Switch modes. Leaving Design unescapes wiki links (KGB). Each instance starts in Design (Plan §5.7: per-instance default). | — | — | — |
| Properties | Collapsible YAML box; "(none)" when absent; placeholder "title: My page" **(KGB)** | not Preview | Edit the front matter | — | — | Inline: "{error}. Saving is still allowed; the graph ignores these properties." **(KGB)** |
| Body | Design: `RadzenMarkdownEditor`. Source: monospace `RadzenTextArea Rows=22`. Preview: `SafeMarkdown.ForPreview` with `CurKb.Links`. `[[links]]` in Preview are clickable: resolved ones open the page, unresolved ones create it (`CreateHref`). | — | — | — | — | — |
| Revert | Light (artboard; KGB Outlined Base) · `undo` | dirty and the page exists | Reload the page from the store | — | — | — |
| Save | Primary · `save`, title "Save (Ctrl+S)" **(KGB)** | not saving, and dirty or not found | `Workspace.SavePageAsync(path, markdown, ticks)` | Conflict: **This page changed** / "{path} was changed since you opened it. Reload to get the latest version, or overwrite it with your text." (WASM; ticks are per tab, D12, so this is a same-tab change) or "{path} was changed outside the app since you opened it. Reload to get the latest version, or overwrite it with your text." (MAUI), with Cancel / Overwrite / Reload **(KGB)** | `Success · "Saved" · 2 s` **(KGB)** | `Warning · "Not saved" / message · 6 s` **(KGB)**; errors via `ShowError` |
| Not-found callout | "This page no longer exists. Your text is still here; save it as a new page to keep it." with **Save as new page** (Primary) and **Close** **(KGB)** | the page was deleted elsewhere | Save as new: the new-page dialog with the current text. Close: clear the editor. | — | — | Toast on load: `Warning · "Page not found" / "{path} doesn't exist." · 6 s` **(KGB)** |
| Changed-elsewhere callout | "This page changed while you were editing. Saving will ask what to keep." (WASM) or "…changed elsewhere…" (MAUI) **(KGB)** | dirty and the stored ticks changed | — | — | — | — |

| State | Spec |
|---|---|
| No page selected, KB not empty | `description` icon, "Select a page or create one" and **New page** (Primary, `note_add`) **(KGB)**. The shortcut list shows Ctrl+S Save, F2 Rename in the tree, Del Delete in the tree **(KGB)**. KGB's chat shortcut and tour and help links are dropped. |
| KB empty (Plan §5.6) | "This story's knowledge base is empty" **(plan)**, sub "Pages you add here are for reference. They are separate from the story's characters, locations and timelines." **(new)**, and **Create first page** **(plan)** (Primary, `note_add`), which runs New page at the root |
| Loading | Skeleton (KGB) |

### 17.4 Links panel

| Control | Style/icon | Enabled when | Action | Success / empty text |
|---|---|---|---|---|
| Header "Links" (`hub`) | — | — | — | — |
| Show in graph | Link · `bubble_chart`, title "Show this page and its links as a graph" **(KGB)** | a page is open | Switch to the Graph view, scope **Around page**, depth 1, centered on this page **(new: KGB navigates to `graph?p=&d=1`)** | — |
| Outgoing {n} | Links (`arrow_outward`) to `PageHref(resolvedPath)` | — | Open the page | "No links to other pages." **(KGB)** |
| Backlinks {n} | Links (`subdirectory_arrow_left`) with "L{line}" | — | Open the page | "No pages link here yet." **(KGB)** |
| Unresolved {n} | `link_off` + raw target, title "No page named {target}"; **Create** button (Light, `add`, aria-label "Create page {target}") **(KGB; artboard style)** | — | `CreatePageAsync` next to the current page; a target with `/` creates the folder first (KGB `WorkspacePage.CreatePageAsync`) | "Every link resolves." **(KGB)** |

### 17.5 Navigation and unsaved changes

| Situation | Behavior |
|---|---|
| Opening another page, leaving the tab, or leaving the workspace with a dirty page | **Unsaved changes** / "This page has unsaved changes. Save them before leaving?" (or the plural form) with Cancel / Discard / Save **(KGB)**. Save that fails, or Cancel, keeps the user where they are and reselects the open page in the tree (KGB `StayOnPage`). |
| `?tab=kb&p=<path>&create=1` | Prevent the navigation and run CreatePage for `<path>` (KGB `OnBeforeNavigationAsync`) |
| `?tab=kb&p=<path>` | Open `<path>`; an invalid path opens nothing |

### 17.6 Graph view (`KnowledgeGraphView`)

| Control | Style/icon | Enabled when | Action | Status / empty text |
|---|---|---|---|---|
| Whole graph / Around page | `RadzenSelectBar` **(KGB, design)** | Around page is disabled with the title "Select a page first" **(KGB)** when no page is open | Rebuild the view. Around page centers on the open page. | — |
| Depth | Dropdown "1 link away", "2 links away", "3 links away" **(KGB)** | Around page | Rebuild | — |
| Missing pages | `RadzenSwitch`, label "Missing pages" **(KGB)**, default on | always | Toggle missing nodes (dashed **(design)**) | — |
| Find a node | `RadzenTextBox`, placeholder "Find a node", title "Enter goes to the next match" **(KGB)** | always | Highlight matches; Enter selects and centers the next one; Esc clears | "No matches" / "{i} of {n}" / "{n} found" **(KGB)** |
| Stat | "{pages} pages · {links} links" plus " · showing the {n} best-connected of {total}" when truncated **(KGB)** | — | — | — |
| Put moved nodes back | Text · `restart_alt`, title "Put moved nodes back" **(KGB)** | at least one node was dragged | Clear pins; rebuild | — |
| Fit to window | Text · `fit_screen`, title "Fit to window" **(KGB)** | always | `asb.fitGraph` | — |
| Node | SVG group; click, Enter or Space selects it; drag moves it | always | Open the details panel | Tooltip: the path, or "{label} — no page yet" **(KGB)** |
| Legend chips | One chip per **top-level folder** (Plan §5.6; the artboard's Characters / Locations / Timelines are sample folders), "Top level" for the root; click hides or shows it; **Show all**; "No page yet"; hint "Scroll to zoom · drag to pan · drag a node to move it" **(KGB)** | always | Rebuild | — |

Details panel:

| Control | Style/icon | Enabled when | Action |
|---|---|---|---|
| Close | Text · `close` | always | Close the panel |
| Open | Primary · `edit_note`, "Open" **(KGB, design)** | a page node | Navigate `PageHref(path)`; the toggle switches to Pages |
| Show around | Light · `hub`, "Show around" **(KGB, design)** | a page node | Scope Around page on that node |
| Create page | Primary · `note_add`, "Create page" **(KGB)** | a missing node; the panel says "No page yet." **(KGB)** | Navigate `CreateHref(label)` |
| Connected {n} / Linked from {n} | List of neighbors (at most 40, then "and {n} more") **(KGB)** | — | Select and center |
| Page preview | `SafeMarkdown.ForPreview`; panel links select a node in the view or open the page in the panel | — | — |

| State | Text |
|---|---|
| Loading | "Laying out the graph…" with a small spinner **(KGB)** |
| Empty | "No pages to show yet." **(KGB)**; with hidden folders: "Every page here is in a hidden folder." and **Show all folders** **(KGB)** |
| Out of view | "This page isn't in the current view. Use Show around to see its links." / "No links in this view." **(KGB)** |

JS bindings return a handle that `DisposeAsync` releases (Plan §5.7).

### 17.7 KB error toasts (`PageCommands.ShowError`, sticky)

| Exception | Summary **(KGB)** | Detail |
|---|---|---|
| `StorageQuotaExceededException` | "Browser storage is full" | "Nothing was saved. Use Export Project on the Details tab, and delete pages you don't need." **(new; KGB points to its own backup in Settings)** |
| `PageTooLargeException` | "Page too large" | ex.Message |
| `KnowledgeAlreadyExistsException` | "Already exists" | ex.Message |
| `InvalidKnowledgePathException` | "Invalid name" | ex.Message |
| `KnowledgeNotFoundException` | "Not found" | ex.Message |
| other | "Something went wrong" | ex.Message |

Name dialog inline errors **(KGB)**: "Names can't contain '/' or '\\'.", "The name is longer than 120 characters.", "'{name}' already exists in this folder.", plus `KnowledgePath.ValidateSegment` messages. A counter "{n} / 120" shows under the field.

### 17.8 Keyboard (Knowledgebase tab)

| Key | Where | Action | Source |
|---|---|---|---|
| Ctrl+S | editor | Save the page | KGB, `asb.js` |
| F2 | tree | Rename the selected node | KGB |
| Del | tree | Delete the selected node | KGB |
| Esc | explorer search, graph find | Clear | KGB |
| Enter | graph find | Next match | KGB |
| Enter or Space | graph node | Select | KGB |

---

## 18. Shortcut summary

| Screen | Keys |
|---|---|
| All dialogs | Esc closes when not busy (§0.3) |
| Paragraph editor | Esc = Close editor (with the unsaved check) |
| Chat | Enter sends, Shift+Enter newline |
| Knowledgebase | §17.8 |
| Everything else | none |

---

## 19. Design-vs-V1 conflicts and how they were resolved

| # | Conflict | Resolution |
|---|---|---|
| 1 | The Settings artboard has no Azure endpoint, Clear key or Test connection | Plan §5.6 and Q8: KGB `LlmSettingsForm` fields plus the probe; the artboard gives layout only |
| 2 | The Utility artboard shows only the fine-tune list; V1 has three tabs | Utility removed (D23, superseded) |
| 3 | V1's Utility note "FineTuning in Azure OpenAI is not supported." is shown for every non-OpenAI provider | Moot: Utility removed (D23) |
| 4 | The artboard's character background types (Personality, Backstory, Relationships) differ from V1's | V1 types (Appearance, Goals, History, Aliases, Facts): they are stored in the files and used by `DetectCharacterAttributes` |
| 5 | The artboard lists all background types at once; V1 filters by the dropdown | Artboard: list everything, with the type in the heading; the dropdown only sets the type for **Add Background** |
| 6 | The timeline dialog artboard lacks V1's Description and shows date-times | Keep Description (it is in `Timelines.csv`); use `ShowTime=true` and the artboard labels "Start Date:" and "Stop Date:" |
| 7 | Entity dialogs in the artboard always show Delete | V1: Delete only for saved items. New dialogs are titled "New …" (Plan §5.6). |
| 8 | The artboard's AI dialog button says "Save" | "Generate" (Plan §5.6) |
| 9 | The artboard selectors have Cancel and Save; V1 has one Close that applies the choice | Artboard buttons; Save applies to the in-popup copy, Cancel discards |
| 10 | The artboard's chapter tools icon is labeled "Add chapter" | Plan §5.6: `note_add` = insert before, tooltip "Insert chapter before" |
| 11 | The artboard always shows chapter Delete | V1: hidden when there is only one chapter |
| 12 | The artboard shows chapter titles ("Chapter One", "A collection of sounds"); V1 chapters have only a name and a synopsis | Eyebrow = V1 `ChapterName`; title = first sentence of the synopsis **(new)** |
| 13 | The artboard shows chapter counts and edit times on story cards; V1 shows titles only | Style plus edit time; no chapter count, so WASM does not open every zip |
| 14 | The artboard's Logs grid has a Date column; V1 log lines have no date | V2 writes a timestamp prefix that V1 can still read **(new)** |
| 15 | V1 keeps a blank editable paragraph at the end of every chapter; the design has only "Add first paragraph" | Added "Add section" at the end of a non-empty chapter **(new)** |
| 16 | V1 Character Save writes first and asks about the rename afterwards | Ask first; No saves nothing **(new order)** |
| 17 | V1 deletes a character background and clears the log without asking | Confirm both (Plan §5.6 "Every Delete asks") |
| 18 | V1 toasts "Calling ChatGPT to detect Attributes..." | Provider-neutral text **(new)** |
| 19 | The artboard's graph legend is Characters / Locations / Timelines | Plan §5.6: color by top-level folder (KGB); the artboard shows sample folders |
| 20 | KGB "Show in graph" and graph links use KGB routes (`graph?p=`, `./?p=`) | D13 and D20: `KbLinkBuilder` links; the Pages/Graph toggle is component state |
| 21 | The Manuscript Editor artboard has its own top bar with Preview and Display settings | Not built; the workspace top bar is used (Q6) |
| 22 | V1 renders prose as `MarkupString` | Plain text with CSS indenting (Plan §9) |
| 23 | V1's Import Story toast is always Info | Success or Error, depending on whether a story was added **(new)** |
| 24 | The Story Workspace artboard puts Chat before Knowledgebase | The owner's order: Knowledgebase, then Chat as the last tab (§9.4, Plan Q19) |
