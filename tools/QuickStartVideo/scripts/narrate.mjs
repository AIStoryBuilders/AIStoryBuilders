// Renders each scene's narration with the ElevenLabs "Michael" voice and records
// each clip's length in src/narration.json. Settings come from the ElevenLabs
// section of an appsettings.json (path in ELEVENLABS_SETTINGS); the key is never printed.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const settingsPath = process.env.ELEVENLABS_SETTINGS;
if (!settingsPath) throw new Error("Set ELEVENLABS_SETTINGS to the appsettings.json that holds the ElevenLabs section.");
const el = JSON.parse(readFileSync(settingsPath, "utf8")).ElevenLabs;
const voiceId = el.MichaelVoiceId || el.VoiceId;
const force = process.argv.includes("--force");

const scenes = JSON.parse(readFileSync(join(root, "scenes.json"), "utf8"));
const audioDir = join(root, "public", "audio");
mkdirSync(audioDir, { recursive: true });

const result = {};
for (const scene of scenes) {
  const out = join(audioDir, `${scene.id}.mp3`);
  if (force || !existsSync(out)) {
    const res = await fetch(
      `${el.BaseUrl}/v1/text-to-speech/${voiceId}?output_format=${el.OutputFormat || "mp3_44100_128"}`,
      {
        method: "POST",
        headers: { "xi-api-key": el.ApiKey, "Content-Type": "application/json", Accept: "audio/mpeg" },
        body: JSON.stringify({
          text: scene.text,
          model_id: el.TtsModelId || "eleven_multilingual_v2",
          voice_settings: {
            stability: el.Stability ?? 0.45,
            similarity_boost: el.SimilarityBoost ?? 0.8,
            style: el.Style ?? 0.25,
            use_speaker_boost: el.SpeakerBoost ?? true,
          },
        }),
      },
    );
    if (!res.ok) throw new Error(`ElevenLabs ${res.status} for ${scene.id}: ${await res.text()}`);
    writeFileSync(out, Buffer.from(await res.arrayBuffer()));
  }
  const seconds = parseFloat(
    execFileSync("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", out]).toString(),
  );
  result[scene.id] = seconds;
  console.log(`${scene.id}: ${seconds.toFixed(2)}s`);
}
writeFileSync(join(root, "src", "narration.json"), JSON.stringify(result, null, 2));
