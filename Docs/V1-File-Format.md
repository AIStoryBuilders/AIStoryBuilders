# AI StoryBuilders V1 File Format Specification

## 1. Purpose

This document specifies, byte for byte where it matters, every file that AI StoryBuilders V1 reads or writes. It is meant to let V2 read existing V1 data and write data that V1 can open, without anyone having to read V1's code.

**Source of truth:** the V1 code at `<repos>/ADefWebserver/AIStoryBuilders`. App version string `03.62.00` ([MauiProgram.cs:61][mauiprogram]); target framework `net10.0-windows10.0.22621.0`. This document describes what that code *does*, bugs included. If this document and the code disagree, the code wins. Recommendations for V2 appear only in §7 and in paragraphs marked **V2:**.

Notation used below:

| Token | Meaning |
|---|---|
| `{Root}` | `Environment.GetFolderPath(SpecialFolder.MyDocuments) + "/AIStoryBuilders"`, usually `%USERPROFILE%\Documents\AIStoryBuilders`. Documents may be redirected to OneDrive. |
| `{Story}` | `{Root}/{Title}`, where `{Title}` is the story title used verbatim as the folder name. |
| `{Temp}` | `Path.GetTempPath() + "AIStoryBuilders"` |
| CRLF / LF / CR | `\r\n` / `\n` / `\r` |
| `[vec]` | A serialized embedding vector, e.g. `[0.0123,-0.0456,...]` with 384 numbers (§5). |
| "field N" | 0-based index after `line.Split('\|')`. |

---

## 2. Folder tree

```
{Root}/
├── AIStoryBuildersStories.csv        story index (one row per story)             §3.1
├── AIStoryBuildersSettings.config    AI provider settings, JSON                  §3.2
├── AIStoryBuildersDatabase.json      model-id → display-alias map, JSON          §3.3
├── AIStoryBuildersLog.csv            application log, plain text, newest first   §3.4
├── ModelCache/                       cached provider model lists                 §3.5
│   └── {servicetype}_{HASH16}.json
├── FineTuneTrainingData/             exists only while a fine-tune upload runs   §3.14
│   └── TrainingData.jsonl
└── {Title}/                          one folder per story
    ├── Timelines.csv                                                             §3.6
    ├── Manifest.config               only in stories imported from .stybld       §3.12
    ├── Characters/
    │   └── {CharacterName}.csv                                                   §3.7
    ├── Locations/
    │   └── {LocationName}.csv                                                    §3.8
    ├── Chapters/
    │   └── Chapter{N}/               N = 1..C, no zero padding
    │       ├── Chapter{N}.txt        chapter synopsis + vector                   §3.9
    │       └── Paragraph{M}.txt      M = 1..P, no zero padding                   §3.10
    └── Graph/                        derived knowledge-graph cache               §3.11
        ├── manifest.json
        ├── graph.json
        └── metadata.json

{Temp}/                               (all three are deleted after use)
├── _Temp/                            .stybld staging, Word export, xlsx export
├── _TempZip/                         {Title}.stybld being built / Import.stybld being read
└── _ManuscriptImport/                manuscript parser scratch dir

FileSystem.AppDataDirectory/          (MAUI app-data folder, NOT under Documents)
├── all-MiniLM-L6-v2.onnx             copied from the app package on first embedding
└── vocab.txt
```

Created at startup only if missing ([MauiProgram.cs:88-185][mauiprogram]): `{Root}`, `ModelCache/`, `AIStoryBuildersLog.csv`, `AIStoryBuildersDatabase.json`, `AIStoryBuildersStories.csv`, `AIStoryBuildersSettings.config`.

A new story ([Story.cs:48-247][story] `AddStory`) creates `{Story}`, `Characters/`, `Chapters/`, `Locations/`, `Timelines.csv`, one `Chapter{N}` folder per generated chapter and one `Paragraph1.txt` per chapter. `Graph/` is created later by `PersistGraphAsync`. Manuscript import ([ManuscriptImport.cs:14-177][manuscriptimport]) creates the same layout. A story imported from `.stybld` gets whatever was in the zip.

---

## 3. File types

### 3.0 Common I/O conventions

Every writer uses one of these .NET APIs. None of them writes a BOM.

| API used by V1 | Encoding | Line ending written | Trailing newline |
|---|---|---|---|
| `File.WriteAllLines(path, lines)` / `WriteAllLinesAsync` | UTF-8, no BOM | Each element is followed by `Environment.NewLine` (CRLF on Windows), the last one included. An empty list gives a 0-byte file. | Yes |
| `File.WriteAllText` / `WriteAllTextAsync` | UTF-8, no BOM | None added; any line breaks are already in the string. | No |
| `File.AppendAllText` | UTF-8, no BOM | None added (callers append `Environment.NewLine` themselves). | As supplied |
| `new StreamWriter(path)` + `WriteLine` | UTF-8, no BOM; truncates the file | CRLF per `WriteLine` | Yes |

On the read side:

| Reader | Behavior |
|---|---|
| `File.ReadAllLines` / `ReadAllText` / `StreamReader` | Detect a UTF-8/UTF-16 BOM, otherwise read as UTF-8. So V1 reads files with or without a BOM. `ReadAllLines` splits on CRLF, LF, or a lone CR and removes the terminator. |
| `ReadCSVFile` ([AIStoryBuildersService.cs:37-55][svc]) | `ReadToEnd().Split('\n')`, which splits on **LF only**, so each line keeps its trailing CR. Drops the final element if it is whitespace, then applies `CleanAIStoryBuildersStoriesCSVFile` (§3.1). Used only for `AIStoryBuildersStories.csv`. |
| Log readers | `ReadToEnd().Split('\n')`, the same as above without the filter. |

Unless stated otherwise, every per-story reader uses `File.ReadAllLines` and then drops lines whose `Trim()` is empty.

**No V1 format has any quoting or escaping.** `|` separates fields, `,` separates names in a paragraph's character list, and a line break ends a record (paragraph files are the only exception). Some writers delete `|` from some fields, as listed per file below. No writer escapes anything. Every split is a plain `string.Split('|')`, with no limit on the number of parts and with empty fields kept.

---

### 3.1 `AIStoryBuildersStories.csv`

| | |
|---|---|
| Path | `{Root}/AIStoryBuildersStories.csv` (a code comment calls it ".json"; the file is `.csv`) |
| Initial content | A single CRLF (2 bytes), written by `StreamWriter.WriteLine("")` ([MauiProgram.cs:151-160][mauiprogram]) |
| Encoding / EOL | UTF-8 without BOM, CRLF, trailing CRLF (`File.WriteAllLines`) |
| Header row | None |

**Record** (one line per story):

```
{Id}|{Title}|{Style}|{Theme}|{Synopsis}|{WorldFacts}
```

| # | Field | Content |
|---|---|---|
| 0 | Id | Decimal integer, see "Id rules" below |
| 1 | Title | Story title. Also used as the folder name `{Story}`. |
| 2 | Style | Free text (the UI labels it style or genre; the graph metadata calls it `genre`) |
| 3 | Theme | Free text. New stories get a fixed system-prompt sentence ([Index.razor:169][index]). |
| 4 | Synopsis | Free text |
| 5 | WorldFacts | Free text. Absent (only 5 fields) on rows written by `.stybld` import. |

**Writers**

| Method | Location | Fields | Sanitizing | Notes |
|---|---|---|---|---|
| `AddStory` | [Story.cs:66-78][story] | 6 | **None** on any field | Re-reads via `ReadCSVFile`, drops blank rows, trims every row, appends the new row, rewrites the whole file. |
| `UpdateStory` | [Story.cs:249-280][story] | 6 | Style, Theme, Synopsis, WorldFacts: CRLF, LF, and CR each become a space, then every `\|` is deleted. Title: none. | Removes every row whose field 1 `==` Title (ordinal, case-sensitive), trims the rest, then **appends the updated row at the end** with a new Id. |
| `DeleteStory` | [Story.cs:282-301][story] | n/a | n/a | Removes rows whose field 1 `==` Title. Does **not** trim, so each kept row still carries the CR left by `ReadCSVFile` and is written as `...\r\r\n`. Then deletes `{Story}` recursively. |
| `ImportProject` | [ExportImport.cs:346-359][exportimport] | **5** (no WorldFacts) | None | Values come from `Manifest.config`. |
| `PersistImportedStoryAsync` | [ManuscriptImport.cs:34-47][manuscriptimport] | 6 | Style, Theme, Synopsis, WorldFacts: line breaks become spaces and `\|` is deleted. Title: none. | Title is the manuscript file name without its extension. |

`UpdateStory` is called by the story editor ([StoryControl.razor:71][storycontrol]), by the chat tool `UpdateStoryDetailsAsync` (which deletes `|` first, [GraphMutationService.cs:642-666][graphmut]), and by `ReloadStoryAsync` step 1 ([ReEmbed.cs:14][reembed]).

**Id rules**

- A new row gets `Id = (number of valid rows currently in the file) + 1`, not max+1.
- An update deletes the row and re-appends it with `Id = (number of other rows) + 1`, so the edited story moves to the end of the file and gets a new Id.
- So Ids are neither stable nor unique. For example: delete Id 1 from {1,2,3}, then add a story, and the new row gets Id 3 again. No V1 code uses Id as a key; the title is the key. The story list is shown in file order. After a `.stybld` import, V1 treats the **last** row as the imported story ([Index.razor:259-264][index]).

**Reader** `GetStorys` ([Story.cs:15-46][story]):

1. `ReadCSVFile`, which splits on LF. Every line still ends in CR (two CRs after a `DeleteStory`).
2. Filter ([AIStoryBuildersService.cs:59-91][svc]): keep a line only if field 0 is non-empty, every character of field 0 passes `char.IsDigit` (Unicode digits included), and `Split('|').Length > 3`.
3. Drop whitespace-only lines and split on `|`. `Id = int.Parse(f[0])`, `Title = f[1]`, `Style = f[2]`, `Theme = f[3]`, `Synopsis = f[4]`, `WorldFacts = f.Length > 5 ? f[5] : ""`. Fields after index 5 are ignored. Nothing is trimmed: **the last field keeps its trailing CR(s)**.
4. If any row throws (for example a row with exactly 4 fields passes the filter but `f[4]` fails, or an Id overflows `int`), the error is logged and **the method returns an empty list**, so every story disappears from the UI.

**User text containing `|` or a line break**

- `|` written by `AddStory` or by import shifts every later field. For example, Synopsis `A|B` is read back as Synopsis=`A` and WorldFacts=`B`, and the real WorldFacts is dropped. A title cannot contain `|` through the UI (§4.1).
- A line break written by `AddStory` or by import splits the row across two physical lines. The second line fails the digit filter and is ignored, so Synopsis is cut at the line break and WorldFacts is lost. The orphan line is physically removed the next time any writer rewrites the file.
- `UpdateStory` flattens line breaks. WorldFacts is therefore effectively one line, even though its consumer splits it on `\n` ([MasterStory.cs:62-64][masterstory]).

Example:

```
1|The Lighthouse Keeper|Gothic mystery|You are a software program that creates prose for novels. ...|A keeper finds a stranger on the rocks.|The island has no electricity.
```

---

### 3.2 `AIStoryBuildersSettings.config` (shape only; V2 will not use it)

The file is JSON despite its extension. It is created by [MauiProgram.cs:162-185][mauiprogram] (`StreamWriter.WriteLine` of a raw string literal) and rewritten by `SettingsService.SaveSettings` ([SettingsService.cs:51-77][settings]) using Newtonsoft `Formatting.Indented` (2-space indent, CRLF, no trailing newline). It is read by `LoadSettings` ([SettingsService.cs:21-49][settings]). The key is stored in plaintext.

```json
{
  "OpenAIServiceOptions": {
    "Organization": "",
    "ApiKey": "<api-key>"
  },
  "ApplicationSettings": {
    "AIModel": "gpt-5-mini",
    "AIType": "OpenAI",
    "Endpoint": "",
    "ApiVersion": ""
  }
}
```

`AIType` is one of `OpenAI`, `Azure OpenAI`, `Anthropic`, `Google AI` ([OrchestratorMethods.cs:94-131][orch]). An empty or missing value is treated as `OpenAI`. A missing `Endpoint` or `ApiVersion` is read as `""`.

---

### 3.3 `AIStoryBuildersDatabase.json`

| | |
|---|---|
| Purpose | Flat JSON object. Each key is an AI model id; each value is the display alias the user gave that model. |
| Initial content | `{`, followed by trailing spaces and a line break, then `}` and CRLF. It comes from a raw string literal, so the inner line break is the source file's line break (CRLF on a Windows checkout with `autocrlf`). Written by [MauiProgram.cs:136-149][mauiprogram]. |
| Writer | `DatabaseService.SaveDatabase` ([DatabaseService.cs:59-76][db]): `JsonConvert.SerializeObject(Dictionary<string,string>, Formatting.Indented)` via `StreamWriter.WriteAsync`. UTF-8 without BOM, 2-space indent, CRLF, **no** trailing newline. Newtonsoft does not escape non-ASCII. |
| Callers | `UpdateModelNameAsync` and `DeleteFineTuneModelAsync` ([OrchestratorMethods.Models.cs:117-201][orchmodels]) |
| Reader | `LoadDatabase` ([DatabaseService.cs:17-57][db]): Newtonsoft dynamic. Each top-level property becomes `(Name, Value.ToString())`. Invalid JSON silently gives an empty map. |

```json
{
  "ft:gpt-4o-mini:example-org::abc123": "My Fine-Tuned Model"
}
```

---

### 3.4 `AIStoryBuildersLog.csv`

Despite the name, this is **not CSV**. It is plain text with one message per line, **newest first**, no delimiter, and **no timestamp on individual entries**.

| Writer | Location | Behavior |
|---|---|---|
| Startup | [MauiProgram.cs:102-134][mauiprogram] | If the file is missing, writes `Application started at {DateTime.Now}` + CRLF. Otherwise reads the whole file, splits on LF, drops the final empty element, and rewrites it as `Application started at {DateTime.Now}` CRLF + `string.Join("\n", oldLines)` + CRLF. No cap is applied here. |
| `WriteToLog` | [LogService.cs:33-64][log] | Reads and splits the same way. If there are more than 1000 elements, keeps the first (newest) 1000. Writes `LogText.Replace("\n", " ")` + CRLF, then `Join("\n", old)` + CRLF. CR is **not** removed from `LogText`. |
| Clear log | [Logs.razor:51-56][logs] | Overwrites the file with `Log cleared on {DateTime.Now}` + CRLF. |

- **Timestamp:** `DateTime.Now.ToString()`, the current culture's general date + long time pattern ("G"), for example `11/5/2026 3:04:05 PM` in en-US. It appears only in the startup line and the clear-log line.
- **Cap:** after `WriteToLog` the file has at most 1001 lines. Startup lines can push it past that until the next `WriteToLog`.
- **Line endings:** splitting on LF keeps each line's CR, so rejoining with LF gives CRLF between lines. The **last (oldest) line gains one extra CR on every rewrite** (`...\r\r\r\n`).
- **Readers:** `LogService.loadLog` ([LogService.cs:17-31][log]) and the Logs page split on LF. Messages are free text, for example `GetStorys: {ex.Message} {ex.StackTrace}` or `Story created {Title}`.

---

### 3.5 `ModelCache/{servicetype}_{HASH16}.json`

| | |
|---|---|
| Writer | `AIModelService.CacheModels` ([AIModelService.cs:322-343][aimodel]): `File.WriteAllText`, UTF-8 without BOM, no trailing newline |
| Reader | `GetCachedModels` ([AIModelService.cs:288-320][aimodel]). Returns nothing if the entry is older than 24 h or `Models` is empty. **Deletes the file** if it fails to parse. |
| File name | `serviceType.ToLowerInvariant().Replace(" ", "-")` + `_` + the first 16 characters of the **uppercase** hex SHA-256 of `UTF8(apiKey.Trim())` + `.json` ([AIModelService.cs:345-357][aimodel]). Examples: `openai_0123456789ABCDEF.json`, `azure-openai_...`, `anthropic_...`, `google-ai_...` |
| Format | System.Text.Json, `WriteIndented = true`, PascalCase property names ([ModelCacheEntry.cs][modelcacheentry]) |

```json
{
  "ServiceType": "OpenAI",
  "Models": [
    "gpt-5-mini",
    "gpt-5"
  ],
  "LastFetched": "2026-11-05T20:04:05.1234567+00:00"
}
```

---

### 3.6 `{Story}/Timelines.csv`

**Record:**

```
{TimelineName}|{TimelineDescription}|{Start}|{Stop}
```

| # | Field | Notes |
|---|---|---|
| 0 | TimelineName | Referenced by exact string from paragraphs, characters, and locations |
| 1 | TimelineDescription | Free text |
| 2 | Start | Culture-formatted date and time, required |
| 3 | Stop | Culture-formatted date and time, or `""` when there is none |

**Writers**

| Method | Location | Date format | Sanitizing / notes |
|---|---|---|---|
| `AddStory` | [Story.cs:148-167][story] | `ToShortDateString() + " " + ToShortTimeString()` | Name passed through `SanitizeFileName` (§4.5). Description raw. Start = Now + i days, Stop = Now + (i+1) days, i = 0, 2, 4, ... `WriteAllLines`. |
| `AddTimeline` | [Story.cs:414-430][story] | **`DateTime?.ToString()`**, i.e. culture "G" **with seconds**. Stop is `""` when null. | `AppendAllText(line + Environment.NewLine)`. No sanitizing. Used by the timeline editor for new rows ([TimelinesEdit.razor:139][tledit]) and by the chat tool (which deletes `\|` from the description). |
| `UpdateTimeline` | [Story.cs:432-474][story] | short date + " " + short time | Rewrites the whole file from `GetTimelines()` (so sorted by Start), minus **every** row whose name equals the target, with the edited row **appended last**. Duplicate names collapse into one row. |
| `UpdateTimelineAndTimelineNameAsync` | [Story.cs:476-514][story] | same | Same rewrite, with the new name. Then cascades (§6.1). |
| `DeleteTimelineAndTimelineNameAsync` | [Story.cs:657-694][story] | same | Same rewrite, without the row. Then cascades (§6.2). |
| `PersistImportedStoryAsync` | [ManuscriptImport.cs:51-67][manuscriptimport] | short date + " " + short time | Name passed through `SanitizeFileName`. Description has `\|` deleted. |

**Culture:** every write uses the **current thread culture** (the Windows user locale) at the time of writing. In en-US the short form is `11/5/2026 3:04 PM` and the `AddTimeline` form is `11/5/2026 3:04:05 PM`. Both can appear in the same file. Culture data comes from the OS (ICU), and some ICU versions put U+202F NARROW NO-BREAK SPACE before AM/PM.

**Reader** `GetTimelines` ([Story.cs:348-412][story]):

- `ReadAllLines`, drop blank lines, split on `|`. Fields 0 to 3 are required; extra fields are ignored.
- `StartDate = DateTime.Parse(f[2])` in the current culture. If that fails, an exception is thrown and **the whole list comes back empty** (logged).
- `StopDate = DateTime.TryParse(f[3])`. A failure or an empty string gives `null`.
- `Id` is the 1-based index among non-blank lines, taken before sorting. The result is sorted by StartDate (stable).
- A row with fewer than 4 fields also empties the whole list.

**`|` / line breaks:** the timeline editor deletes `|` from neither the name nor the description ([TimelinesEdit.razor:85-174][tledit]). A `|` in the description pushes text into field 2, so `DateTime.Parse` fails and **all timelines vanish**. A line break has the same effect.

The UI requires a Start date and requires Start < Stop when Stop is set ([TimelinesEdit.razor:105-133][tledit]). Name uniqueness is not checked.

Example:

```
Morning|The keeper wakes before dawn.|11/5/2026 3:04 PM|11/6/2026 3:04 PM
Storm|The storm reaches the island.|11/9/2026 10:15:30 AM|
```

---

### 3.7 `{Story}/Characters/{CharacterName}.csv`

There is one file per character; the file name is the character name (§4.5). Each line is one "background" entry:

```
{Type}|{TimelineName}|{Description}|{Vector}
```

| # | Field | Notes |
|---|---|---|
| 0 | Type | The UI offers exactly `Appearance`, `Goals`, `History`, `Aliases`, `Facts` ([CharacterEdit.razor:99-106][charedit]; enum `CharacterBackgroundType`, [Enums.cs:10-17][enums]). **Nothing is validated on write or read.** `AddStory` writes whatever the LLM returned (or `""`), manuscript import defaults to `Facts`, and the chat tool accepts any string. Case is kept as written. |
| 1 | TimelineName | `""` means "no timeline". Not checked against `Timelines.csv`. |
| 2 | Description | Free text |
| 3 | Vector | `[vec]` of the Description (§5) |

**Writers**

| Method | Location | Blank line after each record? | Sanitizing |
|---|---|---|---|
| `AddStory` | [Story.cs:90-110][story] | **Yes.** Each element already ends in `Environment.NewLine` and `WriteAllLines` adds another CRLF. | File name passed through `SanitizeFileName(LLM name)`. Description raw. |
| `AddUpdateCharacterAsync` | [Story.cs:1287-1317][story] | Yes | Rewrites the whole file at `SanitizeFileName(originalName)` and re-embeds every entry. Null Type or Timeline becomes `""`. |
| Timeline cascades | [Story.cs:608-647][story], [Story.cs:788-827][story] | Yes | Rewrites **every** character file at `{name-from-filename}.csv` and re-embeds each Description. |
| `PersistImportedStoryAsync` | [ManuscriptImport.cs:69-89][manuscriptimport] | No | Type defaults to `Facts` |
| `ReEmbedCsvDescriptionFile` | [ReEmbed.cs:198-240][reembed] | No | Lines with 3 or more fields become `f0\|f1\|f2\|[new vec]`; any 5th or later field is dropped. A line with exactly 2 fields is treated like a location line (`f0\|f1\|[vec of f0]`). A line with 1 field is **dropped**. A line whose field 2 looks like a vector is kept verbatim. |

The UI save callers are [CharacterEdit.razor:149][charedit] and the paragraph auto-detect in [ParagraphEdit.razor:441][paredit]. The chat tools `AddCharacterAsync` and `UpdateCharacterAsync` ([GraphMutationService.cs:48-122][graphmut]) call `AddUpdateCharacterAsync` with **one** background, which **replaces the whole file** with that single entry.

**Pipe and line-break handling:** the background editor deletes `|` from Description ([CharacterBackgroundEdit.razor:81][charbgedit]), and so does the chat tool. `AddStory` and manuscript import do not. **No path handles line breaks.** A line break inside a Description splits the record, and the remainder is read as a separate entry with garbage fields. A `|` in a Description cuts it short, and the rest lands in the vector field. V1 never parses character vectors, so this does not crash.

**Reader** `GetCharacters` ([Story.cs:1165-1285][story]):

- `Directory.GetFiles(Characters, "*.csv", SearchOption.AllDirectories)`, which **includes subfolders**, in OS enumeration order (not sorted).
- `CharacterName` = file name without extension. `ReadAllLines`, drop blank lines, split on `|`.
- f[0] is Type, f[1] is TimelineName, f[2] is Description, f[3] is VectorContent (kept as a string). Each missing field defaults to `""`; extra fields are ignored. This reader never throws per line.
- If Description, after `TrimStart`, starts with `[` and contains `,`, it is treated as a misplaced vector: it moves to VectorContent and Description becomes `""` ([Story.cs:1247-1255][story]).
- `Character.Id` is the 1-based file index. `Id` and `Sequence` of each background are the 1-based record index.

Example (written by the UI; note the blank lines):

```
Appearance||Tall, with salt-grey hair.|[0.0123,-0.0456,...]

History|Storm|Lost her brother at sea.|[0.0311,0.0027,...]

```

---

### 3.8 `{Story}/Locations/{LocationName}.csv`

```
{Description}|{TimelineName}|{Vector}
```

| # | Field | Notes |
|---|---|---|
| 0 | Description | Free text |
| 1 | TimelineName | `""` means none |
| 2 | Vector | `[vec]` of the Description |

**Writers**

| Method | Location | Blank lines? | Notes |
|---|---|---|---|
| `AddStory` | [Story.cs:112-146][story] | Yes | Writes `desc\|\|[vec]`; the timeline is always empty. If the LLM gave no descriptions, writes one record that uses the location *name* as the description. File name passed through `SanitizeFileName`. |
| `AddLocationAsync` | [Story.cs:964-1002][story] | Yes | File name passed through `SanitizeFileName(LocationName)`. Rewrites the whole file. Timeline is `""` when null. |
| `UpdateLocationDescriptions` | [Story.cs:1004-1042][story] | Yes | Same as above |
| Timeline cascades | [Story.cs:562-602][story], [Story.cs:742-782][story] | Yes | Rewrites **every** location file at `{name-from-filename}.csv` and re-embeds |
| `PersistImportedStoryAsync` | [ManuscriptImport.cs:91-115][manuscriptimport] | No | Timeline always `""`. A null description becomes the location name. |
| `ReEmbedCsvDescriptionFile` | [ReEmbed.cs:228-235][reembed] | No | Writes `f0\|f1\|[new vec]`. Lines with fewer than 2 fields are dropped. |

The UI editor trims the location **name** and deletes `|` from it ([LocationEdit.razor:186-190][locedit]). It deletes nothing from descriptions ([LocationDescriptionEdit.razor:77-100][locdescedit]). The chat tool deletes `|` from descriptions.

**Reader** `GetLocations` ([Story.cs:839-921][story]):

- `GetFiles("*.csv", AllDirectories)`. The name comes from the file name. `ReadAllLines`, drop blank lines, split on `|`.
- f[0] is Description and f[1] is the timeline name. A Timeline object is always created, possibly with name `""`. f[2] and later are ignored.
- **A non-blank record with no `|` throws IndexOutOfRange, and the whole `GetLocations` returns an empty list**: every location of the story disappears.
- A Description that looks like a vector (`[` ... `,`) is cleared to `""`.
- So a line break inside a description, which leaves a pipe-less first fragment, wipes out every location. A `|` inside a description cuts it short, and the remainder becomes the timeline name.

`LocationExists` ([Story.cs:923-962][story]) compares names **case-insensitively** against the file names. The UI uses it to block duplicate names when adding.

Example:

```
A stone tower on a rocky island.||[0.0123,-0.0456,...]

The lower floor floods at high tide.|Storm|[0.0201,-0.0110,...]

```

---

### 3.9 `{Story}/Chapters/Chapter{N}/Chapter{N}.txt`

The file holds a single record and **no trailing newline** (`File.WriteAllText`):

```
{Synopsis}|{Vector}
```

**Writers**

| Method | Location | Notes |
|---|---|---|
| `AddStory` | [Story.cs:192-206][story] | Written only if the LLM's `chapter_synopsis` is not null. **The folder is created either way**, so a folder without its `.txt` file is possible. The LLM's chapter name is discarded. |
| `AddChapterAsync` | [Story.cs:1592-1609][story] | Appends a chapter (from [ChaptersEdit.razor:75][chedit], or the chat tool when the position is past the end). A null synopsis becomes `" "` (one space). |
| `InsertChapterAsync` | [Story.cs:1611-1629][story] | Folder name = `ChapterName.Replace(" ", "")`. The UI passes `"Chapter{N}"` with synopsis `"New Chapter"` ([ChaptersControl.razor:285-297][chcontrol]). |
| `UpdateChapterAsync` | [Story.cs:1631-1640][story] | Rewrites the file and re-embeds |
| `PersistImportedStoryAsync` | [ManuscriptImport.cs:127-135][manuscriptimport] | Written only if the synopsis is not whitespace, so a folder without its `.txt` file is possible |
| `ReEmbedStory` / `ReloadStoryAsync` | [ReEmbed.cs:155-169][reembed], [ReEmbed.cs:57-70][reembed] | `text = ReadAllText().Trim()`; `synopsis = text` up to the first `"\|["`, or all of `text` if there is none. Writes `synopsis\|[vec]`. |

Sanitizing: only the chat tools delete `|`. The UI writes the synopsis exactly as typed, including `|` and LF line breaks.

**Reader** `GetChapters` ([Story.cs:1498-1567][story]):

- `Directory.GetDirectories(Chapters)`. Every sub-folder must be named `Chapter` + integer: the code inserts a space at index 7 and calls `int.Parse`. Anything else throws, and **the story shows no chapters**.
- Reads `{folder}/{folderName}.txt` with `ReadAllLines`. **A missing file throws, and the story shows no chapters.**
- Synopsis = the text before the first `|` on the first non-blank line. So a multi-line synopsis is read as its first line only, and a `|` cuts it short. A vector-looking synopsis is cleared.
- `ChapterName` = `"Chapter {N}"`, `Sequence` = N. Folders are first string-sorted, then sorted by N.

Example: `The keeper finds a stranger on the rocks.|[0.0123,-0.0456,...]`

---

### 3.10 `{Story}/Chapters/Chapter{N}/Paragraph{M}.txt`

```
{LocationName}|{TimelineName}|[{Char1},{Char2},...]|{Content}|{Vector}
```

| # | Field | Notes |
|---|---|---|
| 0 | LocationName | Exact location name, `""` for none, or `Unknown` (written by manuscript import) |
| 1 | TimelineName | Exact timeline name, `""`, or `Unknown` |
| 2 | Characters | `[` + names joined with `,` (no spaces added, no quoting) + `]`. `[]` means none. |
| 3 | Content | Prose. **May contain line breaks**, unlike every other field in V1. |
| 4 | Vector | `[vec]` of Content, or `""` for a new empty paragraph. Always the last field, so it sits on the **last** physical line. |

**Writers**

| Method | Location | Output |
|---|---|---|
| `AddStory` | [Story.cs:208-242][story] | Writes `Paragraph1.txt` only. Location and Timeline are the LLM value cut at its first `,` (not trimmed). Characters are `[]` when `character_names` is null. **When it is an empty array, the field is a lone `]`**: the code that strips the trailing comma removes the `[` instead. `WriteAllText`, no trailing newline. |
| `AddParagraph` | [Story.cs:1898-1926][story] | First renumbers (§4.4), then writes `{loc}\|{tl}\|[{chars}]\|\|`, i.e. empty content **and** an empty vector, 5 fields, ending in `\|\|`. Used by the UI "add above" ([ParagraphEdit.razor:549][paredit]) and the chat tool. |
| `UpdateParagraph` | [Story.cs:1928-1955][story] | Builds `{loc}\|{tl}\|[{chars}]\|{content}\|[vec]` and then replaces **every `\n` with `\r\n`** across the whole string, so an existing CRLF becomes `\r\r\n`. Null names become `""`. `WriteAllText`, no trailing newline. Used by [ParagraphEdit.razor:484][paredit] and the chat tool. |
| `PersistImportedStoryAsync` | [ManuscriptImport.cs:137-160][manuscriptimport] | Location and Timeline are `Unknown` when no match was found. Content is written raw: LF line breaks, possibly blank lines (`\n\n`), no conversion to CRLF. |
| `ReEmbedStory` / `ReloadStoryAsync` | [ReEmbed.cs:133-152][reembed], [ReEmbed.cs:24-43][reembed] | Reads the **first non-blank line only**, needs at least 4 fields, and skips files whose content is whitespace. Writes `f0\|f1\|f2\|{f3}\|[vec]`. **A multi-line paragraph is truncated to its first line, and anything after a `\|` in the content is lost.** |
| Cascades | §6 | `ReadAllLines`, drop blank lines, edit fields on the **first line**, then `WriteAllLines`. Result: CRLF between lines, a trailing CRLF, and no blank lines. |

The UI does **not** delete `|` from paragraph content; only the chat tool does ([GraphMutationService.cs:593][graphmut]). The UI deletes `|`, `[`, and `]` from character names it creates ([ParagraphEdit.razor:382-384][paredit], [CharacterEdit.razor:134-136][charedit]). Commas in names are not touched.

**Reader** `GetParagraphs` ([Story.cs:1655-1731][story]):

- `GetFiles(chapterFolder, "Paragraph*.txt", AllDirectories)`. M = `int.Parse` of the text after `Paragraph` (the code inserts a space at index 9). A non-numeric suffix, such as `Paragraph1 - Copy.txt`, throws, and **the chapter shows no paragraphs**.
- `ReadAllLines`, drop whitespace-only lines, `string.Join("\n", lines)`, `Split('|')`. f[0] is location, f[1] timeline, f[2] characters, f[3] content. At least 4 fields are required, or the whole chapter comes back empty. f[4] is not read here. Results are sorted by M.
- **Content as read:** lines are joined with LF, **blank lines inside the paragraph are lost**, and the content stops at its first `|`.
- Characters: `ParseStringToList` ([OrchestratorMethods.cs:270-277][orch]) removes every `[` and `]` anywhere in the field and splits on `,`, with **no trimming and no removal of empty entries**. So `[]` gives a list with one empty name `""`, `]` gives the same, and `[Ada, Tom]` gives `"Ada"` and `" Tom"`. The graph builder ignores empty names and `Unknown`.

**Vector reader** `GetAllParagraphVectorsForChapter` ([Story.cs:1744-1788][story]), used to find related paragraphs when writing:

- Takes the **first** non-blank line: f[1] is the timeline, f[3] the content, f[4] the vector, parsed with `Newtonsoft.JsonConvert.DeserializeObject<float[]>`. An empty f[4] is skipped.
- In a multi-line paragraph the first line has only 4 fields, so `f[4]` throws. The exception is caught **outside** the loop, so **every remaining paragraph in that chapter is skipped**.
- `GetParagraphVectorsInternal` ([Story.cs:1790-1868][story]) behaves the same way but returns an empty list on any exception.

Examples:

```
Lighthouse|Morning|[Ada,Tom]|Ada climbed the stairs.|[0.0123,-0.0456,...]
```

Multi-line, as written by `UpdateParagraph` (␍␊ = CRLF; there is no trailing newline after the vector):

```
Lighthouse|Morning|[Ada]|The lamp was dark.␍␊She lit it anyway.|[0.0123,-0.0456,...]
```

A new empty paragraph: `Lighthouse|Morning|[Ada]||`. With no characters: `||[]||`.

---

### 3.11 `{Story}/Graph/manifest.json`, `graph.json`, `metadata.json`

**This is a derived cache.** All three files are rebuilt from the CSV/TXT files by `GraphBuilder.Build` ([GraphBuilder.cs:12-321][graphbuilder]) and written by `PersistGraphAsync` ([AIStoryBuildersService.cs:117-154][svc]).

Persist is called from:
- `EnsureGraphExistsAsync`, only when `graph.json` is missing (new story, `.stybld` import)
- the chat panel's `BuildGraph` ([ChatControl.razor:168-186][chat])
- `ReloadStoryAsync`
- manuscript import
- `BuildTimelineSummaryAsync`, when the in-memory graph is dirty or missing ([MasterStory.cs:79-107][masterstory])

Chat mutations refresh only the **in-memory** graph ([GraphMutationService.cs:693-727][graphmut]), so `graph.json` is often stale. The only reader is `LoadGraphFromDisk` ([AIStoryBuildersService.cs:158-173][svc]), used by `EnsureGraphExistsAsync`. A parse failure returns null, and the graph is then rebuilt.

**Serializer** (`GraphJsonOptions`, [AIStoryBuildersService.cs:108-113][svc]):

- System.Text.Json with `WriteIndented = true`: 2-space indent, `Environment.NewLine` (CRLF), `"name": value`.
- `PropertyNamingPolicy = CamelCase`. Dictionary keys are **not** renamed; they appear exactly as the code wrote them (already camelCase).
- Enums are camelCase strings via `JsonStringEnumConverter(CamelCase)`.
- The default `JavaScriptEncoder` escapes **all non-ASCII and HTML-sensitive characters** as uppercase `\uXXXX`. For example `'` → `'`, `"` → `"`, `&` → `&`, `<` → `<`, `>` → `>`, `+` → `+`, `` ` `` → ```, `é` → `é`, `—` → `—`. A newline is `\n`.
- `File.WriteAllTextAsync`: UTF-8 without BOM, no trailing newline.
- Reading needs camelCase property names (matching is case-sensitive). Enum strings match case-insensitively and integers are accepted. Unknown properties are ignored.

**`manifest.json`**

| Property | Type | Value |
|---|---|---|
| `storyTitle` | string | `story.Title ?? ""` |
| `createdDate` | string | `DateTime.UtcNow.ToString("o")`, e.g. `2026-11-05T20:04:05.1234567Z`. This is the time of the persist, not story creation. |
| `version` | string | App version, `03.62.00` |
| `nodeCount` | number | `graph.Nodes.Count` |
| `edgeCount` | number | `graph.Edges.Count` |

**`metadata.json`**

| Property | Type | Value |
|---|---|---|
| `title` | string | Title |
| `genre` | string | **Story.Style** |
| `theme` | string | Theme |
| `synopsis` | string | Synopsis |
| `chapterCount`, `characterCount`, `locationCount`, `timelineCount` | number | Counts of what was loaded |

**`graph.json`** ([GraphModels.cs][graphmodels]):

```json
{
  "storyTitle": "The Lighthouse Keeper",
  "nodes": [
    {
      "id": "character:ada",
      "label": "Ada",
      "type": "character",
      "properties": {
        "role": "Appearance",
        "backstory": "Tall, with salt-grey hair.; Lost her brother at sea."
      }
    }
  ],
  "edges": [
    {
      "id": "character:ada--HAS_ATTRIBUTE--attribute:character:ada:appearance:1",
      "sourceId": "character:ada",
      "targetId": "attribute:character:ada:appearance:1",
      "label": "HAS_ATTRIBUTE",
      "properties": {}
    }
  ]
}
```

Definitions used in the id formats below: `Normalize(x)` = `Regex.Replace(x.Trim(), @"[\s_-]+", " ")`, and `lower` = `ToLowerInvariant`. An entity is skipped when its name is null or whitespace, or equals `Unknown` (any case) ([GraphBuilder.cs:384-389][graphbuilder]). Node ids are de-duplicated case-insensitively, and the first occurrence wins.

**Node types** (`type` is one of `character`, `location`, `timeline`, `chapter`, `paragraph`, `attribute`):

| `type` | `id` format | `label` | `properties` (all string values) |
|---|---|---|---|
| `character` | `character:{lower(Normalize(name))}` | `Normalize(name)` | `role` = Type of the first background entry; `backstory` = the non-empty descriptions joined with `"; "`. A character that appears in a paragraph but has no file gets an implicit node with both values `""`. |
| `attribute` (character) | `attribute:character:{lower(name)}:{lower(Type ?? "Facts")}:{seq}` | `"{Type}: {description cut to 60 chars + "..."}"` | `parentType`=`character`, `parentName`, `attributeType`, `description`, `timeline`, `sequence`. `seq` is the 1-based record index; entries with a blank description are skipped but still counted. |
| `location` | `location:{lower(Normalize(name))}` | `Normalize(name)` | `description` = the first description |
| `attribute` (location) | `attribute:location:{lower(name)}:description:{seq}` | `"Description: ..."` | `parentType`=`location`, `parentName`, `attributeType`=`Description`, `description`, `timeline`, `sequence` |
| `timeline` | `timeline:{lower(Normalize(name))}` | `Normalize(name)` | `description`; `startDate` and `endDate` as `DateTime.ToString("o")` with no offset (e.g. `2026-11-05T15:04:00.0000000`), or `""` |
| `chapter` | `chapter:{lower(Normalize(ChapterName))}`, e.g. `chapter:chapter 1` | `Chapter 1` | `index` = Sequence, `beatsSummary` = synopsis |
| `paragraph` | `paragraph:{lower(chapterName)}:p{M}`, e.g. `paragraph:chapter 1:p3` | `P3` | `text` (full content, with `\n`), `location`, `timeline` (raw names) |

**Edge labels.** Edge `id` is `{sourceId}--{label}--{targetId}`, de-duplicated case-insensitively. Edge `properties` is always `{}`.

| `label` | Source → target | Created when |
|---|---|---|
| `HAS_ATTRIBUTE` | character or location → attribute | For each non-blank background or description |
| `IN_TIMELINE` | attribute → timeline | The attribute has a valid timeline. Can point to a timeline that has no node. |
| `CONTAINS` | chapter → paragraph | For every paragraph |
| `MENTIONED_IN` | character → paragraph | The character is in the paragraph's list |
| `APPEARS_IN` | character → chapter | Same condition |
| `SEEN_AT` | character → location | The paragraph's location is valid |
| `INTERACTS_WITH` | character → character | For each unordered pair in one paragraph. The source is the name that sorts first (OrdinalIgnoreCase). |
| `SETTING_OF` | location → chapter | The location is used by a paragraph in that chapter (creates the location node if missing) |
| `COVERS` | timeline → chapter | The timeline is used by a paragraph in that chapter (creates the timeline node if missing) |
| `ACTIVE_ON` | character → timeline | For each background with a valid timeline. Can point to a timeline that has no node. |

Names in a paragraph's character list are matched to known characters in this order: exact match ignoring case, then substring in either direction, then Levenshtein distance ≤ 2; otherwise the name is kept as-is (and gets an implicit node). Nodes and edges are written in insertion order: characters, each followed by its attributes; locations with their attributes; timelines; then, per chapter, the chapter node, its paragraph nodes and implicit nodes.

---

### 3.12 `.stybld` export/import and `Manifest.config`

A `.stybld` file is a standard ZIP archive (`System.IO.Compression.ZipFile`).

**Export** `ExportProject` ([ExportImport.cs:152-247][exportimport]):

1. Delete and re-create `{Temp}/_Temp` and `{Temp}/_TempZip`.
2. Write `_Temp/Manifest.config`: `JsonConvert.SerializeObject(JSONManifest)` (compact, one line) via `StreamWriter.WriteLine`. UTF-8 without BOM, followed by CRLF.
3. `CopyDirectory({Story} → _Temp)` ([ExportImport.cs:442-470][exportimport]) copies **every** file and sub-folder, overwriting. **If the story folder holds its own `Manifest.config` (true of every story imported from `.stybld`), that stale copy overwrites the fresh manifest from step 2.**
4. `ZipFile.CreateFromDirectory(_Temp, _TempZip/{Title}.stybld)`: Optimal (Deflate) compression, base directory not included, entry names relative with `/` separators, entries only for empty directories.
5. Return the bytes and delete both temp folders. The UI suggests the name `{Title}_{yyyy-MM-dd_HH-mm-ss}.stybld` ([StoryEdit.razor:319][storyedit]).

Zip layout (everything at the root):

```
Manifest.config
Timelines.csv
Characters/{Name}.csv ...
Locations/{Name}.csv ...
Chapters/Chapter{N}/Chapter{N}.txt
Chapters/Chapter{N}/Paragraph{M}.txt ...
Graph/manifest.json, Graph/graph.json, Graph/metadata.json    (if present)
...any other file that happened to be in {Story}
```

**`Manifest.config`** ([JSONManifest.cs][manifestmodel]). Newtonsoft, PascalCase, one line; null values are written as `null`.

| Property | Value written |
|---|---|
| `Version` | App version, `03.62.00` |
| `Title` | Story title (becomes the folder name on import) |
| `Style`, `Theme`, `Synopsis` | From the in-memory story |
| `ExportedDate` | `DateTime.Now.ToString()`: current culture, "G" |

WorldFacts is **not** exported and is lost on a round trip.

```
{"Version":"03.62.00","Title":"The Lighthouse Keeper","Style":"Gothic mystery","Theme":"You are a software program ...","Synopsis":"A keeper finds a stranger on the rocks.","ExportedDate":"11/5/2026 3:04:05 PM"}
```

**Import** `ImportProject` ([ExportImport.cs:251-375][exportimport]):

1. Delete and re-create the temp folders. Write the bytes to `_TempZip/Import.stybld` and extract them to `_Temp`.
2. Read `_Temp/Manifest.config` with `ReadAllText` and Newtonsoft (property names match case-insensitively). If it is missing or invalid, the exception message is returned to the UI.
3. **Version check:** reject if `ConvertToInteger(file.Version) > ConvertToInteger(app.Version)`. `ConvertToInteger` ([ExportImport.cs:474-494][exportimport]) splits on `.` and sums `Convert.ToInt32(segment[i]) * {10000, 100, 1}[i]`. So `03.62.00` → 36200, and `3.7` → 30700. **More than 3 segments throws**, as do non-numeric segments and a null `Version`; in each case the import fails.
4. Target folder = `{Root}/{manifest.Title}`. **The Title is not validated.** If the folder already exists, the import is rejected with a message.
5. `CopyDirectory(_Temp → {Story})` copies everything at the zip root, **including `Manifest.config`**, which stays in the story folder.
6. Append the stories row `{count+1}|{Title}|{Style}|{Theme}|{Synopsis}` (5 fields, no sanitizing).
7. The UI then calls `EnsureGraphExistsAsync` for the **last** row of the stories file ([Index.razor:259-264][index]). It loads `Graph/graph.json` if the zip contained one; otherwise it builds and persists a new graph.

---

### 3.13 Word export (`.docx`, export only)

`ExportWordDocument` ([ExportImport.cs:78-148][exportimport]) uses Xceed DocX. It builds `{Temp}/_Temp/{Title}.docx`, reads the bytes, and deletes `_Temp`. The UI saves the result as `{Title}_{yyyy-MM-dd_HH-mm-ss}.docx` ([StoryEdit.razor:299][storyedit]).

Document content:
- A title paragraph: 25 pt, 50 pt spacing after, centered.
- For each chapter in `GetChapters` order: a section page break, then `Chapter {N}` at 20 pt with 25 pt spacing after, centered.
- For each paragraph: the content with `&nbsp;` replaced by a space and `<br />` replaced by `\n`, then split on `\n`. Each piece is trimmed and becomes one Word paragraph with a 28 pt first-line indent.

V1 never reads this file back. Manuscript import reads `.docx` with its own parser.

---

### 3.14 Training data

**`{Root}/FineTuneTrainingData/TrainingData.jsonl`** ([UtilityClass.razor:342-353][utility]):

- Written only during "Upload to OpenAI", with `File.WriteAllLinesAsync`: UTF-8 without BOM, CRLF after every line including the last.
- Deleted about 2 s after a successful upload, and the folder is removed if it is empty. It stays on disk if the upload throws.
- One line per training example, serialized by System.Text.Json ([Conversation.cs:31-43][conversation], [Message.cs][message]): compact output, `role` as a camelCase enum name, `name` left out when null, default encoder escaping (`'` → `'`, and so on).

```
{"messages":[{"role":"system","content":"You are a fiction novel writing software program that creates prose from the story beats provided."},{"role":"user","content":"<story beats>"},{"role":"assistant","content":"<one line of prose>"}]}
```

The examples come from `CreateTrainingDataAsync` ([ExportImport.cs:17-72][exportimport]): one per `\n`-separated line of every paragraph. The user message is the LLM's story beats for that line.

The same JSONL format is also produced from an Excel template and saved via a file dialog as `FineTunedJson.jsonL` ([UtilityClass.razor:217-280][utility]).

`ExportTrainingData` ([ExportImport.cs:379-436][exportimport]) writes an EPPlus workbook. The content is xlsx, but the UI names the file `{Title}-TrainingData.xls`. It has a sheet named `Training Data`, headers `System`, `User`, `Assistant` in row 1, and data from row 2.

---

## 4. Naming rules

### 4.1 Story title / folder

- Folder = `{Root}/{Title}`, verbatim.
- Validation exists in the UI only ([StoryEdit.razor:231-245][storyedit]):
  - Reject any character in `Path.GetInvalidPathChars() + Path.GetInvalidFileNameChars()`. On Windows that is `" < > | : * ? \ /` and U+0000–U+001F. The error message also mentions `;`, but `;` is allowed.
  - `TrimInnerSpaces` collapses any run of 2 or more whitespace characters into one space. Leading and trailing spaces are **not** trimmed.
  - For new stories, reject a title that matches an existing one case-insensitively.
- V1 has no story rename.
- `.stybld` import and manuscript import do no title validation.
- Title matching is inconsistent: exact and case-sensitive in `UpdateStory`, `DeleteStory`, and `EnsureGraphExistsAsync`; case-insensitive in `LoadFullStory`.

### 4.2 Chapters

| Concept | Format | Source |
|---|---|---|
| Folder and file | `Chapter{N}` / `Chapter{N}.txt`: no space, no padding, N ≥ 1 | everywhere |
| Display name (`Chapter.ChapterName`) | `Chapter {N}`: the folder name with a space inserted at index 7 | [Story.cs:1523][story] |
| Display name → folder | `ChapterName.Split(' ')`, then `[0] + [1]`, or `Replace(" ", "")` | e.g. [Story.cs:1661-1662][story] |
| Sequence | N parsed from the folder name | |

Expected invariant: the folders are exactly `Chapter1` to `ChapterC` with no gaps. The renumbering code assumes that the folder count equals the highest N.

### 4.3 Paragraphs

`Paragraph{M}.txt`, M = 1..P, contiguous, no padding. `CountParagraphs` counts the files that match `Paragraph*.txt` ([Story.cs:1870-1896][story]).

### 4.4 Insert, delete, and renumber (exact rename order)

| Operation | Steps | Source |
|---|---|---|
| Insert paragraph at P | `n = CountParagraphs()`; for `i = n` down to `P`: `File.Move(Paragraph{i}, Paragraph{i+1})`; then write the new `Paragraph{P}.txt`. | [Edit.cs:23-33][edit], [Story.cs:1898-1926][story] |
| Delete paragraph P | `File.Delete(Paragraph{P})`; `n = CountParagraphs()` (now P−1 fewer); for `i = P` to `n`: `File.Move(Paragraph{i+1}, Paragraph{i})`. | [Story.cs:1957-1977][story], [Edit.cs:34-44][edit] |
| Insert chapter at S | `c = folder count`; for `i = c` down to `S`: first rename the file `Chapter{i}/Chapter{i}.txt` → `Chapter{i}/Chapter{i+1}.txt`, then the folder `Chapter{i}` → `Chapter{i+1}`. Then `InsertChapterAsync` creates `Chapter{S}/Chapter{S}.txt`. | [Edit.cs:66-80][edit], [ChaptersControl.razor:285-297][chcontrol], [GraphMutationService.cs:454-458][graphmut] |
| Delete chapter S | Delete folder `Chapter{S}` recursively (`ForceDeleteDirectory`); `c = folder count`; for `i = S` to `c`: rename the file `Chapter{i+1}/Chapter{i+1}.txt` → `Chapter{i+1}/Chapter{i}.txt`, then the folder `Chapter{i+1}` → `Chapter{i}`. | [Story.cs:1642-1651][story], [Edit.cs:81-95][edit], [ChaptersControl.razor:340-342][chcontrol] |
| Append chapter | `Chapter{count+1}` via `AddChapterAsync` | [ChaptersControl.razor:216-223][chcontrol] |

Any `Move` failure is caught and logged and the loop stops, leaving the files partly renumbered. Causes include a gap in the numbering, a missing `Chapter{i}.txt`, or a target that already exists. Paragraph files inside a moved chapter folder are not renamed or rewritten. Graph ids embed the chapter name, so `graph.json` goes stale until the next persist.

### 4.5 Character file names

- `SanitizeFileName` ([OrchestratorMethods.cs:242-255][orch]) removes every character in `Path.GetInvalidFileNameChars()`, plus `|`. It does **not** trim, change case, handle reserved names (a `ReservedNames` array is declared but never used), or limit length. Commas and brackets survive; the UI deletes `[ ] |` from names itself.
- Used by `AddStory`, `AddUpdateCharacterAsync` (applied to the *original* name), and manuscript import.
- **Not** used by rename (the new file is `{CharacterName.Trim()}.csv`) or by delete and the rename source (both use the raw original name).
- Paragraph character lists store the unsanitized and untrimmed name, so a paragraph reference can differ from the file name.
- On read, the character name **is** the file name. Windows file names are case-insensitive, so `Ada.csv` and `ada.csv` are the same file. V1's UI has no duplicate check for new characters, so saving a new character with an existing name overwrites that file.

### 4.6 Location file names

`AddLocationAsync` and `UpdateLocationDescriptions` use `SanitizeFileName(LocationName)`. Rename moves the file to the raw new name, which the UI has trimmed and stripped of `|`. The UI blocks duplicates case-insensitively for new locations. The location name is the file name.

### 4.7 Timeline names

Timelines have no file of their own. A name lives in field 0 of `Timelines.csv` and is referenced by exact, case-sensitive string from paragraph field 1, character field 1, and location field 1. `AddStory` and manuscript import pass names through `SanitizeFileName`; the timeline editor does nothing to them. Uniqueness is not enforced, but `UpdateTimeline` collapses duplicates.

---

## 5. Vector serialization

`GetVectorEmbedding(text, combine)` ([OrchestratorMethods.cs:46-61][orch]):

```csharp
var v = (await _embeddingGenerator.GenerateAsync(new[] { text }))[0].Vector.ToArray();   // float[384]
var s = "[" + string.Join(",", v) + "]";
return combine ? text + "|" + s : s;
```

| Aspect | Behavior |
|---|---|
| Model | all-MiniLM-L6-v2 ONNX, run locally ([LocalEmbeddingGenerator.cs][embed]). Input is lower-cased and split on space, tab, CR, and LF, then greedily WordPiece-tokenized (an unknown word becomes `[UNK]`), with at most 254 tokens plus `[CLS]`/`[SEP]`. Output is mean-pooled over all tokens and L2-normalized. **384 dimensions.** |
| Brackets and separator | `[` and `]`, values separated by `,` with no spaces |
| Number format | `float.ToString()` with **no format and no culture argument**. That is the shortest round-trippable form in the **current culture**: up to 9 significant digits, no trailing zeros, exponent form for small magnitudes (e.g. `1.2345678E-05`, `-3E-06`). |
| Culture | Current culture. Where the decimal separator is `,` (de-DE, fr-FR, ...), the output is `[0,0123,-0,0456,...]`, which is ambiguous. V1's reader would silently parse it as twice as many numbers. |
| `combine = true` | Returns `{text}\|{vector}`, with the text raw (unsanitized). This is how Description/Synopsis/Content and the vector get written together. |
| Empty input | Still produces a full 384-dim vector (from `[CLS][SEP]` alone). V1 never writes `[]`. |
| Empty vector field | Only in new paragraphs from `AddParagraph` (the line ends in `\|\|`). `ReEmbed` skips paragraphs with whitespace-only content, so they stay empty. |
| Where vectors live | Character field 3, location field 2, chapter: after the first `\|`, paragraph field 4 (last field, last physical line) |
| Readers | Only paragraph vectors are ever parsed, with `JsonConvert.DeserializeObject<float[]>` (invariant culture, exponents accepted) ([Story.cs:1773][story]). Chapter, character, and location vectors are never parsed by V1; they are only regenerated. Dimensions are not validated. Cosine similarity ([MasterStory.cs:283-303][masterstory]) loops over the 384-dim query and indexes the stored vector with the same index, so **a shorter stored vector throws**. |
| Size | About 4–5 KB of text per vector |

---

## 6. Cascades

In every cascade, "rewrite" of a paragraph means: `ReadAllLines`, drop blank lines, edit the **first line** split on `|`, rejoin it with `|`, `WriteAllLines`. Side effects: CRLF between lines, a trailing CRLF, and blank lines removed. All string comparisons are ordinal and **case-sensitive**. No cascade updates `Graph/*.json`.

| Operation | Entry point | Files changed |
|---|---|---|
| **Timeline rename** | `UpdateTimelineAndTimelineNameAsync` ([Story.cs:476-655][story]). Called from [TimelinesEdit.razor:156][tledit] when the name changed and the user confirmed, and from chat `RenameTimelineAsync`. | 1. `Timelines.csv` rewritten (§3.6). 2. Each paragraph whose field 1 `==` old name: field 1 = new name (only matching files are rewritten). 3. **Every** location file rewritten and **every record re-embedded**; timeline replaced where `==` old. 4. **Every** character file the same. Location and character files get the blank-line pattern; vector-looking descriptions are written back as `""`. An exception stops the cascade part-way (it is logged). |
| **Timeline delete** | `DeleteTimelineAndTimelineNameAsync` ([Story.cs:657-835][story]) | As for rename, except the row is removed and matching references become `""`. |
| **Timeline edit (same name)** | `UpdateTimeline` ([Story.cs:432-474][story]) | `Timelines.csv` only |
| **Location rename** | `UpdateLocationName` ([Story.cs:1102-1160][story]); the UI then calls `UpdateLocationDescriptions` | Each paragraph whose field 0 `==` old name: field 0 = new name. Then `File.Move(Locations/{old}.csv → Locations/{new}.csv)`. If the move fails, the paragraphs are already changed (error logged). Prose is **not** rewritten. Character and timeline files are untouched. |
| **Location delete** | `DeleteLocation` ([Story.cs:1044-1100][story]) | Paragraphs whose field 0 `==` name: field 0 = `""`. Then `Locations/{name}.csv` is deleted. |
| **Character rename** | `UpdateCharacterNameAsync` ([Story.cs:1394-1488][story]). From [CharacterEdit.razor:149-167][charedit], after the file has first been re-saved under the **old** name, and from chat `RenameCharacterAsync`. | **Every** paragraph file is rewritten, even if nothing changed: (a) field 2: remove all `[` `]`, split on `,`, replace entries `==` old with the new name (untrimmed), rejoin as `[a,b]`. (b) Prose: if there are more than 3 fields, `field3 = Regex.Replace(field3, @"\b" + Regex.Escape(old) + @"\b", new)`. This is case-sensitive, whole-word, and replaces every occurrence, **but only on the first physical line**; later lines of a multi-line paragraph are not changed. `\b` will not match at a name edge that is a non-word character (e.g. `Dr.`). (c) If the prose changed and there are more than 4 fields (single-line paragraphs only), the last field gets a new vector of field 3. Then `File.Move(Characters/{old}.csv → Characters/{new.Trim()}.csv)`. The character file's contents do not change. There is **no try/catch**, so a paragraph with fewer than 3 fields, or an existing target file, aborts the cascade part-way and the error reaches the UI. |
| **Character delete** | `DeleteCharacter` ([Story.cs:1319-1392][story]) | **Every** paragraph file is rewritten: entries `==` name and empty entries are removed from field 2 (so the `]` bug becomes `[]`). Prose is untouched. Then `Characters/{name}.csv` is deleted. No try/catch. |
| **Character save** | `AddUpdateCharacterAsync` | Its own file only, fully rewritten and re-embedded |
| **Chapter insert / delete** | §4.4 | Folder and file renames only. Deleting a chapter deletes all its paragraphs. No content is rewritten. |
| **Paragraph insert / delete** | §4.4 | File renames, plus the new empty file `{loc}\|{tl}\|[chars]\|\|` on insert |
| **Story update** | `UpdateStory` | The stories row moves to the end and gets a new Id (§3.1) |
| **Story delete** | `DeleteStory` | The row is removed, then `{Story}` is deleted recursively: attributes reset to Normal, each delete retried 5 times with backoff (for OneDrive locks) ([Story.cs:303-344][story]) |
| **Re-embed / Reload story** | `ReEmbedStory`, `ReloadStoryAsync` ([ReEmbed.cs][reembed]) | Rewrites every paragraph file (**multi-line paragraphs are truncated to their first line**), every chapter file, and every character and location file. Reload also runs `UpdateStory` first and `PersistGraphAsync` last. |

---

## 7. Gotchas for V2

1. **Culture-dependent output.** Timeline dates, log timestamps, `Manifest.config` `ExportedDate`, and **every vector float** are formatted with the Windows user's current culture. There is no invariant culture anywhere in V1. A file written under en-US means something different, or fails to parse, under en-GB or de-DE. **V2 (plan D30):** write timeline dates in V1's current-culture short form, so V1 reads them correctly, but write vectors with `CultureInfo.InvariantCulture`; on any comma-decimal culture, V1's own vectors are already corrupt. When reading, try the current culture first, then fall back to en-US and invariant; treat any Unicode whitespace (including U+202F) before AM/PM as a space; and validate that a vector has exactly 384 numbers.
2. **`|` is never escaped.** V1's UI fails to delete it from paragraph content, chapter synopsis, timeline name and description, and location descriptions, and `AddStory`/import do not delete it from LLM or manifest text. Effects on V1 read: content cut short, fields shifted, and in some files the whole collection lost (§3.6, §3.8). **V2:** delete `|` from every free-text field before writing. When reading paragraphs, consider treating the last field as the vector only if it starts with `[`, and joining any extra middle fields back into the content. Note that this differs from V1's own reader.
3. **Line breaks.** Paragraph content is the only field that may span lines; on disk it is CRLF from `UpdateParagraph` but LF from manuscript import. Everywhere else a line break corrupts the record, yet V1 itself writes them in a few paths: synopsis via `AddStory`, chapter synopsis from the UI, character and location descriptions. V1 drops blank lines inside paragraphs when reading.
4. **V1 mishandles multi-line paragraphs, which it creates itself.** Re-embed truncates them to the first line; character rename rewrites prose only on the first line; vector retrieval throws and skips the rest of the chapter. If V2 writes multi-line paragraphs, those V1 operations will damage them.
5. **One bad record hides everything.** All of these empty a whole collection in V1:
   - a stories row with exactly 4 fields
   - an unparseable timeline Start date
   - a location line without `|`
   - a non-`ChapterN` folder under `Chapters/`
   - a missing `ChapterN.txt`
   - a `Paragraph*.txt` with a non-numeric suffix
   - a paragraph with fewer than 4 fields

   **V2:** never write any of these, and make V2's own readers tolerate bad records one at a time.
6. **The title is the folder name.** V1 validates it only in the UI and not on import, and it allows trailing spaces and dots, reserved device names (`CON`, `NUL`, ...), and very long names. **V2:** validate on create and on import (no `..`, no path separators), and enforce case-insensitive uniqueness.
7. **Case sensitivity.** NTFS treats names case-insensitively, but every V1 cascade compares with ordinal case-sensitive matching. The graph and `LocationExists` match case-insensitively. Renaming `ada` to `Ada` with `File.Move` works on NTFS, but `Ada` and `ADA` in paragraph lists are different strings to the cascades.
8. **CRLF, trailing newlines, and BOM.** Match the per-file tables in §3: `WriteAllLines` files end with CRLF; chapter files and single-line paragraph files have **no** trailing newline. V1 never writes a BOM but reads one fine, so V2 should write without a BOM. Readers that split on LF (stories, log) leave a trailing CR, so strip `\r` from the end of every field.
9. **Blank lines** between character and location records are normal; most V1 writers produce them.
10. **Story Ids are unstable and can repeat.** Key on Title, keep file order, and expect the last row to be the newest import.
11. **Character lists:** `[]` parses to one empty name, `AddStory` can write `]`, a name containing `,` splits into two, and nothing is trimmed. **V2:** drop empty entries and keep commas out of character names.
12. **Import version gate.** V1 refuses a `.stybld` whose Manifest `Version` is greater than `03.62.00`, and crashes on versions with more than 3 numeric segments. **V2:** to produce exports V1 can import, write `Version` as `03.62.00` or lower, with at most 3 numeric segments.
13. **Stale `Manifest.config`.** Imported stories keep `Manifest.config` in their folder, and V1 re-exports that stale copy in place of a fresh one. WorldFacts is never carried by `.stybld`.
14. **`Graph/*.json` is a cache.** It is often stale, and V2 can rebuild it at any time. To match V1 byte for byte, use System.Text.Json with indented output, camelCase names, camelCase enum strings, the default (escaping) encoder, CRLF, and no trailing newline.
15. **Sentinels:** manuscript import writes `Unknown` for a missing paragraph location or timeline, and the graph ignores `Unknown`.
16. **Subfolders count.** `Characters/` and `Locations/` are enumerated with `AllDirectories`, so a `.csv` in any subfolder becomes a character or location.
17. **The log is not CSV.** It is newest first, has no per-entry timestamps, and the oldest line accumulates CRs.
18. **Embedding compatibility.** Similarity is only meaningful if V2's vectors come from the same model and pipeline: all-MiniLM-L6-v2, V1's simplified lower-case WordPiece tokenizer, mean pooling, L2 normalization, 384 dimensions. Otherwise re-embed.
19. **Names in paragraphs are not sanitized, but file names are.** The location or character named in a paragraph can differ from the file name; for example, a name containing `?` loses that character in the file name.
20. **Chat "update character/location" replaces the file** with a single entry, discarding the other backgrounds or descriptions. This is V1 behavior, not a format rule.

<!-- link references (relative to Docs/) -->
[mauiprogram]: ../../../ADefWebserver/AIStoryBuilders/MauiProgram.cs
[svc]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.cs
[story]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.Story.cs
[edit]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.Edit.cs
[exportimport]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.ExportImport.cs
[reembed]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.ReEmbed.cs
[masterstory]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.MasterStory.cs
[manuscriptimport]: ../../../ADefWebserver/AIStoryBuilders/Services/AIStoryBuildersService.ManuscriptImport.cs
[log]: ../../../ADefWebserver/AIStoryBuilders/Services/LogService.cs
[db]: ../../../ADefWebserver/AIStoryBuilders/Services/DatabaseService.cs
[settings]: ../../../ADefWebserver/AIStoryBuilders/Services/SettingsService.cs
[graphbuilder]: ../../../ADefWebserver/AIStoryBuilders/Services/GraphBuilder.cs
[graphmut]: ../../../ADefWebserver/AIStoryBuilders/Services/GraphMutationService.cs
[graphmodels]: ../../../ADefWebserver/AIStoryBuilders/Models/GraphModels.cs
[manifestmodel]: ../../../ADefWebserver/AIStoryBuilders/Models/JSON/JSONManifest.cs
[enums]: ../../../ADefWebserver/AIStoryBuilders/Models/Enums.cs
[modelcacheentry]: ../../../ADefWebserver/AIStoryBuilders/Models/ModelCacheEntry.cs
[conversation]: ../../../ADefWebserver/AIStoryBuilders/Models/Conversation.cs
[message]: ../../../ADefWebserver/AIStoryBuilders/Models/Message.cs
[orch]: ../../../ADefWebserver/AIStoryBuilders/AI/OrchestratorMethods.cs
[orchmodels]: ../../../ADefWebserver/AIStoryBuilders/AI/OrchestratorMethods.Models.cs
[aimodel]: ../../../ADefWebserver/AIStoryBuilders/AI/AIModelService.cs
[embed]: ../../../ADefWebserver/AIStoryBuilders/AI/LocalEmbeddingGenerator.cs
[utility]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/UtilityClass.razor
[logs]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Logs.razor
[index]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Index.razor
[storyedit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Story/StoryEdit.razor
[storycontrol]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Story/StoryControl.razor
[charedit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Characters/CharacterEdit.razor
[charbgedit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Characters/CharacterBackgroundEdit.razor
[locedit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Locations/LocationEdit.razor
[locdescedit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Locations/LocationDescriptionEdit.razor
[tledit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Timelines/TimelinesEdit.razor
[chcontrol]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chapters/ChaptersControl.razor
[chedit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chapters/ChaptersEdit.razor
[paredit]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Paragraphs/ParagraphEdit.razor
[chat]: ../../../ADefWebserver/AIStoryBuilders/Components/Pages/Controls/Chat/ChatControl.razor
