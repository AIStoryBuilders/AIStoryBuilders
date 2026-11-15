# Development environment

How to set up, run and verify AIStoryBuildersV2. It goes with [AIStoryBuildersV2-Plan.md](AIStoryBuildersV2-Plan.md).
This file contains no secrets. Supply keys only through environment variables or the app's Settings page.

## 1. Toolchain

| Item | Version | Notes |
|---|---|---|
| Windows | 10.0.19041 or later | The MAUI app's Windows TFM is `net11.0-windows10.0.19041.0` |
| .NET SDK | `11.0.100` | Pinned in `global.json` (plan §6.6) with `rollForward: latestFeature`. It replaced the `11.0.100-rc.3.26522.148` pin when the GA SDK arrived through a Visual Studio update and removed rc.3. The NuGet packages are on .NET 11 GA too. The .NET 10 SDKs can stay installed. |
| MAUI workload | `maui-windows 11.0.0/11.0.100` (workload version `11.0.100.1`) | CLI-managed (`dotnet workload list`). It sets `$(MauiVersion)` to 11.0.0. Workloads belong to an SDK band, so a new SDK band needs the workload installed again (§2). |
| Visual Studio | 18.x with the .NET MAUI workload | Optional for the Web app, practical for MAUI debugging |
| Aspire | `Aspire.AppHost.Sdk 13.6.4`, `Aspire.Hosting.Maui 13.6.4-preview.1.26523.11` | Dev-time only (plan G1). The `aspire` CLI is optional. |
| Node.js | not required | Use any static file server for the published WASM smoke test (§4). `npx serve -s` is one option. |

Check the toolchain:

```bash
dotnet --list-sdks
```

```bash
dotnet workload list
```

## 2. Workloads (T0)

Workloads are installed per SDK band. When a new SDK band is installed (for example, a Visual Studio update that installs a new SDK), the MAUI workload must be installed again for that band. If it isn't, the MAUI build fails with `NETSDK1147` and asks for `maui-tizen`. That message is misleading: install `maui-windows`, and don't run the `dotnet workload restore` it suggests, which would also pull in the Tizen and mobile workloads.

Use one of these:

1. From the repo root, in an **elevated** (administrator) terminal, so that `global.json` selects the pinned SDK:

   ```bash
   dotnet workload install maui-windows
   ```

2. Alternatively, open **Visual Studio Installer**, choose **Modify**, and keep the **.NET MAUI** workload selected.

Then check with `dotnet workload list` and the MAUI build command in AGENTS.md.

Only `maui-windows` is needed by default. The mobile targets are built only with `-p:IncludeMobileTargets=true`.

## 3. Running the apps

| App | Command | Where it runs |
|---|---|---|
| WASM, without Aspire | `dotnet run --project AIStoryBuildersV2.Web --launch-profile http` | `http://localhost:5022` (`https://localhost:7213` with the `https` profile), from [launchSettings.json](../AIStoryBuildersV2.Web/Properties/launchSettings.json) |
| MAUI, without Aspire | Visual Studio: set the startup project to `AIStoryBuildersV2` and the target to **Windows Machine**. CLI: `dotnet build AIStoryBuildersV2/AIStoryBuildersV2.csproj -f net11.0-windows10.0.19041.0 -t:Run` | A desktop window |
| Both, with Aspire | `aspire run` from the repo root (it reads `aspire.config.json`), or run `AIStoryBuildersV2.AppHost` | Dashboard on the AppHost profile ports. Resources: `aistorybuildersv2-windows` (start it from the dashboard) and `web`. |

Visual Studio may create `AIStoryBuildersV2/AIStoryBuildersV2.csproj.user` to remember the active debug framework (it should be `net11.0-windows10.0.19041.0`). The file is user-local and not in the repo, so don't commit it.

## 4. Publishing and serving the WASM app

```bash
dotnet publish AIStoryBuildersV2.Web -c Release
```

Serve `AIStoryBuildersV2.Web/bin/Release/net11.0/publish/wwwroot` from any static server. The server must:

- fall back to `index.html` for unknown paths
- serve `.wasm` as `application/wasm`, and serve `.onnx` and `.dat` as `application/octet-stream`
- keep the `<base href>` correct when the app runs under a sub-path. Test this once with a sub-path, because D18 depends on it.

## 4a. Building the Microsoft Store package

The Windows app ships as an update to V1's Store listing (plan Q25). Its identity is in `AIStoryBuildersV2/Platforms/Windows/Package.appxmanifest`. Before each submission, raise `Identity/@Version` above the version that is live in the Store, and keep its last part `0`.

```bash
dotnet publish AIStoryBuildersV2/AIStoryBuildersV2.csproj -f net11.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=MSIX -p:RuntimeIdentifierOverride=win-x64 -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never
```

The package is `AIStoryBuildersV2/AppPackages/AIStoryBuildersV2_<version>_x64_Test/AIStoryBuildersV2_<version>_x64.msix`. It is unsigned: upload it in Partner Center (**Packages**), which signs it. The `_Test` folder name comes from the sideload build mode; the `.msix` is the same one a Store build would make.

To try the package locally without replacing an installed V1, unzip it, change `Identity/@Name` in `AppxManifest.xml` to a test name, delete `AppxSignature.p7x`, `AppxBlockMap.xml` and `[Content_Types].xml`, and run `Add-AppxPackage -Register <folder>\AppxManifest.xml` (Developer Mode must be on). Remove it afterwards with `Get-AppxPackage <test name> | Remove-AppxPackage`. It uses the real `Documents\AIStoryBuilders`, so §8 applies.

## 5. Steps that need a person

An agent can't complete these on its own. Leave them unchecked and report them (AGENTS.md, "How to work").

| Task | Step | What the person does |
|---|---|---|
| T0 | Workload update | §2 |
| T2b | Golden fixtures | Copy `AIStoryBuildersV2.Tests/Fixtures/V1Story/before/` into `Documents\AIStoryBuilders\` on a machine with V1 installed. Do each operation in [Test-Data.md](Test-Data.md) §3 in the V1 UI. Copy each result back to `after-<operation>/` and commit it. **Back up your own V1 stories first**, and remove the fixture story afterwards. |
| T5, T6, T7b, T16 | Live provider checks | Set the §6 variables, or enter a key in Settings |
| T6 | A model that fails the probe | Run Ollama locally with a model that has no tool support, set `OLLAMA_ORIGINS` (§7), and try to verify it |
| T9a, T10a, T18 | V1 coexistence | Open a V2-edited story in V1 and the reverse, and import V2's `.stybld` into V1 |
| T11, T15 | Visual match | Compare with the artboards at 1440 px |
| T12b | Speech-to-text in WebView2 | Try the microphone button in the MAUI app |
| T19e | Writing voice link | **Done (2026-11-15).** The owner clicked **Get your lexical fingerprint** in the MAUI app and it opened in the system browser. The rest of the MAUI pass was done the same day: upload, refusal of a bad file, replace with confirm, remove with confirm, the Invalid card and chip, the paragraph dialog note, Delete removing the file, and the `09b-details-writing-voice.png` screenshot. |
| T19e | V1 round trip | Export a story with a fingerprint, import it into V1, export it from V1, import it into V2, and check **Writing voice** shows it |
| T19f | Live style check | [Test-Data.md](Test-Data.md) §7, row "T19 voice" |
| Q25 | Store submission | Try the packaged app (§4a), then upload the `.msix` in Partner Center, update the listing's description and screenshots, and submit |

## 6. Environment variables (opt-in live tests)

Live tests are skipped unless these are set. Never commit values.

| Variable | Example value | Used by |
|---|---|---|
| `ASB_LIVE_PROVIDER` | `Anthropic`, `OpenAI`, `AzureOpenAI`, `GoogleAI` or `Ollama` | T7b, T16 integration tests |
| `ASB_LIVE_KEY` | `<api-key>` | same |
| `ASB_LIVE_MODEL` | `<model-id>` (must be one that passes the probe) | same |
| `ASB_LIVE_ENDPOINT` | `https://<resource>.openai.azure.com/` or `http://<ollama-host>:11434/` | Azure and Ollama only |
| `ASB_FINGERPRINT_SAMPLE` | `C:\path\to\my-fingerprint.json` (a real file from the fingerprint tool; never commit it) | `RealFingerprintSampleTests` (T19): parses, saves and renders it, and prints the style card to the test output |

One more variable is not a live test. It rewrites committed test fixtures, so review the diff before committing:

| Variable | Value | Used by |
|---|---|---|
| `ASB_UPDATE_GOLDEN` | `1` | Rewrites `Fixtures/Fingerprint/valid-bare.card.txt` (`FingerprintPromptRendererTests`) after a deliberate change to the style card |
| `ASB_REGENERATE_FIXTURES` | `1` | Rewrites `Fixtures/V1Story/before/` (`StoryReadTests`), `Fixtures/V1Story/graph` (`GraphTests`) and `Fixtures/embeddings.json` (`EmbeddingTests`). Those tests also write the graph and embeddings files when they are missing. |

PowerShell, for the current session only:

```powershell
$env:ASB_LIVE_PROVIDER = 'Anthropic'; $env:ASB_LIVE_KEY = '<api-key>'; $env:ASB_LIVE_MODEL = '<model-id>'
```

## 7. Provider notes for the browser (WASM)

| Provider | Browser requirement |
|---|---|
| Anthropic | Works directly. The client sends the `anthropic-dangerous-direct-browser-access` header. |
| OpenAI | Works directly (Online does this today) |
| Azure OpenAI | The resource must allow the app's origin in CORS **(assumed; verify in T6)** |
| Google AI | The key goes in a header (D8). CORS is verified in T6. |
| Ollama | Start Ollama with `OLLAMA_ORIGINS` set to the app's origin, for example `http://localhost:5022` |

## 8. Where data lives during development

| App | Location | How to reset |
|---|---|---|
| MAUI | `Documents\AIStoryBuilders`, **shared with V1** (D4) | Back it up first. Delete only test stories, never the whole folder. |
| MAUI settings | Preferences `asb.llm.*` and SecureStorage `asb.llm.apiKey` | Settings → Clear key |
| WASM | `localStorage` keys `asb2:*` for the dev origin | DevTools → Application → Storage → Clear site data |
