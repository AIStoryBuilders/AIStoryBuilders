# AIStoryBuildersV2 — instructions for AI coding agents

> **Start with [AGENTS.md](../AGENTS.md) and [Docs/AIStoryBuildersV2-Plan.md](../Docs/AIStoryBuildersV2-Plan.md).** They
> take precedence over this file.

This is a .NET 11 MAUI (Windows) + Blazor WebAssembly app sharing the `AIStoryBuildersV2.Shared`
Razor class library (same structure as KnowledgeGraphBuilder).

**Build the UI from the design.** The screens are specified in `Docs/Design/DESIGN.md` and the
interactive artboards (`Docs/Design/*.dc.html`). Implement components/pages in
`AIStoryBuildersV2.Shared` so both hosts get them.

Guidance:
- Use **Radzen.Blazor** components and Material icon names (the design maps Radzen `ButtonStyle`s
  and icons explicitly). Reuse the design tokens in `AIStoryBuildersV2.Shared/wwwroot/app.css`.
- Register shared UI services in `Shared/Hosting/ServiceCollectionExtensions.cs`; register
  host-specific services (file storage, settings, AI keys) in `MauiProgram.cs` and
  `AIStoryBuildersV2.Web/Program.cs`.
- Behavioural reference for the domain (story/chapter/paragraph model, manuscript import,
  multi-provider AI, knowledge graph): the existing AIStoryBuilders V1 app (local path in AGENTS.md).
- The Knowledgebase tab follows KnowledgeGraphBuilder (local path in AGENTS.md).
- Only the Windows TFM (`net11.0-windows10.0.19041.0`) builds by default. Pass `-p:IncludeMobileTargets=true`
  to add Android/iOS/MacCatalyst (iOS/MacCatalyst need macOS).
