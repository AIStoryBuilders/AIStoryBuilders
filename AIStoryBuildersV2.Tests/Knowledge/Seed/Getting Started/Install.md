---
title: Install
tags: [start, setup]
---
# Install

Knowledge Graph Builder runs in two independent ways. They don't share data.

## Windows desktop app

- Run the **KnowledgeGraphBuilder** app (or `dotnet build -t:Run -f net10.0-windows10.0.19041.0` from the repository).
- Pages are plain `.md` files under `Documents\KnowledgeGraphBuilder`. You can edit them with any editor; the app notices changes when you save.
- The API key is kept in Windows secure storage.

## Browser app

- Serve the published `wwwroot` folder from any static file server, or run `dotnet run --project KnowledgeGraphBuilder.Web`.
- Pages are stored in this browser's local storage. Use **Settings → Export** to keep a backup, because clearing site data removes them.
- The API key is stored unencrypted in this browser and is sent only to the provider you choose.

## API key

Open **Settings**, pick a provider and model, enter your API key, and click **Test connection**.

Back to [[Welcome]].
