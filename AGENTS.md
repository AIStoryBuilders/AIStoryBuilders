# AIStoryBuildersV2: instructions for AI coding agents

This repository is being rebuilt from a scaffold into AI StoryBuilders V2: a .NET 11 MAUI (Windows) app and a
Blazor WebAssembly app that share one Razor class library. **Read these before writing any code:**

| Read | For |
|---|---|
| [Docs/AIStoryBuildersV2-Plan.md](Docs/AIStoryBuildersV2-Plan.md) | **The plan.** Decisions (§3), design (§5), contracts (§6), the task list (§12) and acceptance criteria (§13). If this file and the plan disagree, the plan wins. |
| [Docs/V1-File-Format.md](Docs/V1-File-Format.md) | The exact story file formats. V2 must stay byte-compatible with them. |
| [Docs/UI-Behavior-Spec.md](Docs/UI-Behavior-Spec.md) | What every button, dialog and message does |
| [Docs/Design/DESIGN.md](Docs/Design/DESIGN.md) and `Docs/Design/*.dc.html` | Layout, tokens and styling |
| [Docs/Test-Data.md](Docs/Test-Data.md) | Fixture story, probe fixture, KB test page and expected results |
| [Docs/Dev-Environment.md](Docs/Dev-Environment.md) | SDK, workloads, environment variables and how to run each app |

## How to work

1. **One task at a time, in the plan's §12 order.** Each task (T0, T1, T2a, …) is one commit or PR. Don't start a task until the previous one's *done when* check passes.
2. Before you start a task, re-read its plan sections and the decisions it cites (D1–D32).
3. When a task is done:
   - run the three commands below
   - tick its checkbox in plan §12
   - list in the commit message anything you could not verify, such as checks that need a person (Dev-Environment §5)
4. **When the docs don't answer a question:**
   - use the default in plan §14 if there is one
   - otherwise pick the option closest to V1's behavior, and record it as a new row in plan §14 marked "decided by implementer"
   - don't stop and wait
5. Some steps need a person (Dev-Environment §5). Don't fake them: leave the step unchecked and say why.

```bash
dotnet build AIStoryBuildersV2/AIStoryBuildersV2.csproj -f net11.0-windows10.0.19041.0
```

```bash
dotnet build AIStoryBuildersV2.Web/AIStoryBuildersV2.Web.csproj
```

```bash
dotnet test AIStoryBuildersV2.Tests/AIStoryBuildersV2.Tests.csproj
```

## Hard rules

| Rule | Why (plan ref) |
|---|---|
| All pages, components and app logic go in `AIStoryBuildersV2.Shared`. The MAUI and Web projects contain host services only, with no `.razor` files. | G3 |
| **Never change V1's file formats.** The only new thing allowed inside a story folder is `{Title}/KnowledgeBase/`. Never read or write `AIStoryBuildersSettings.config` or `ModelCache/`. | D4, D7 |
| Write vectors with `CultureInfo.InvariantCulture`, and timeline dates in V1's current-culture short form | D30 |
| Before writing story files, delete `\|` from free text and remove line breaks from every field except paragraph content. Readers skip a bad record; they never drop the whole list. | D32 |
| Never check whether a story exists with `Directory.Exists`. Ask `IStoryLibraryStore`, because in WASM, MEMFS holds only the open story. | D5, D25 |
| Every method in the plan's §6.2 MarkDirty list calls `IStoryPersistence.MarkDirty(title)` after its last write. Add any new writing method to that list and to its test. | §6.2 |
| Never call `ZipFile.ExtractToDirectory`. Extract entry by entry, with the Zip Slip guard. | D22 |
| Validate story titles with `StoryTitle.Validate` when a story is created, renamed or imported. Existing V1 folders are grandfathered (D29). | D29 |
| No sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`) in Shared code | D25 |
| No static mutable state. Use `StoryGraphCache` with `StoryGraphContext`, and give each story its own KB. | D17, D26, D13 |
| Every AI call gets its client from `IChatClientProvider.GetClientAsync(model)`, and only verified models are offered. The one exception is the capability probe, which builds its own client from the unsaved settings (§5.9). | D9, D10 |
| KB tools available to story AI are prefixed `kb_` and offered only when **Search knowledgebase** (paragraph AI prompt) or **Include Knowledgebase** (Story Chat) is checked, except Story Chat's `kb_delete_page` and `kb_delete_folder`, which are always offered when the KB has pages (Q21). Story Chat's page edits (`kb_create_page`, `kb_update_page`, `kb_delete_page`) and folder tools (`kb_create_folder`, `kb_rename_folder`, `kb_move_folder`, `kb_delete_folder`) only propose; nothing is written until the user clicks Apply. The paragraph retriever gets read tools only. | D14, Q19, Q20, Q21 |
| No CDN or third-party scripts and no external fonts. Vendor everything into `wwwroot`. | D18, §9 |
| Never put API keys, passwords, real hostnames or personal data in code, tests, logs, docs or commits | §9 |
| Use Radzen.Blazor components (pinned at `[12.1.9]`), Material icon names and the `--asb-*` tokens in `Shared/wwwroot/app.css` | DESIGN.md |
| Register shared services in `Shared/Hosting/ServiceCollectionExtensions.cs`, and host services in `MauiProgram.cs` or `AIStoryBuildersV2.Web/Program.cs` | §5.3 |

## Reference code (read-only, outside this repo)

The plan links to these as sibling checkouts. If you can't reach them, stop and ask. Don't rebuild V1 from memory.

| Name | Path relative to this repo | Use it for |
|---|---|---|
| V1 | `../../ADefWebserver/AIStoryBuilders` | Features and behavior. **Source of truth.** |
| Online | `../AIStoryBuildersOnline` | Browser-safe adaptations: the zip lifecycle, browser embeddings, REST AI clients |
| KGB | `../../KnowledgeGraphBuilder` | Solution structure, AI Settings code, Knowledge Base code and tests |

Don't modify the reference repositories.

## Build notes

- After T0, the iOS, Android and Mac Catalyst targets build only when `-p:IncludeMobileTargets=true` is passed, and iOS and Mac Catalyst need macOS. Build the Windows TFM and the Web project on Windows.
- .NET 11 is a release candidate here. Package versions are in plan §6.6. Don't move to newer prereleases unless a build needs it, and if you do, say why in the commit.
