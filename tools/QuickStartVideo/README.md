# AI StoryBuilders 4.0 quick start video

A Remotion project that turns the user-guide screenshots into a narrated quick start
video. The narration is made with ElevenLabs text-to-speech.

| File | What it holds |
|---|---|
| `scenes.json` | Scene order, the narration text, the screenshot for each scene and where its slow zoom aims |
| `public/images/` | Copies of the screenshots from `Docs/images/user-guide/`. In `05-new-story-filled.png` an error message was painted out. |
| `public/audio/` | Narration clips, one per scene, from `npm run narrate` |
| `public/opener.mp4` | The 10-second opener clip. It is not in git: copy `AIInnovative Opener_1080p.mp4` from the project's video assets here before you render. |
| `public/logo-reveal.mp4` | The 7-second closing clip. It is not in git: copy `Minimal Logo Reveal_1080p.mp4` from the project's video assets here before you render. |
| `src/` | The compositions: `QuickStart` (with captions) and `QuickStartNoCaptions` (for sites that add their own captions) |

## Make the video

```bash
npm install
```

Narration (only when `scenes.json` text changes). `ELEVENLABS_SETTINGS` points to an `appsettings.json` with an
`ElevenLabs` section (`BaseUrl`, `ApiKey`, `MichaelVoiceId` or `VoiceId`, `TtsModelId`, voice settings). The key is
read from that file and never printed.

```bash
node scripts/narrate.mjs --force
```

Render both versions, then make copies under 10 MB for upload forms:

```bash
npm run render
```

```bash
npm run render:nocaptions
```

```bash
npm run web
```

The output goes to `out/`, which is not in git. Use `npm run studio` to preview and tweak scenes.
