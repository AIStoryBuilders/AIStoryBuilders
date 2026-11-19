// Re-encodes each rendered MP4 to a "-web" copy of at most ~9.5 MB, small enough for
// browser upload tools that cap files at 10 MB. Two-pass x264 at a bitrate worked out
// from the clip's length.
import { execFileSync } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";

const TARGET_BYTES = 9.5 * 1000 * 1000;
const AUDIO_KBPS = 96;

for (const input of process.argv.slice(2)) {
  const seconds = parseFloat(
    execFileSync("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", input]).toString(),
  );
  const videoKbps = Math.floor((TARGET_BYTES * 8) / 1000 / seconds - AUDIO_KBPS - 20);
  const output = input.replace(/\.mp4$/, "-web.mp4");
  const log = join(tmpdir(), "quickstart-x264");
  const common = ["-y", "-v", "error", "-i", input, "-c:v", "libx264", "-preset", "veryslow", "-b:v", `${videoKbps}k`, "-pix_fmt", "yuv420p", "-passlogfile", log];
  execFileSync("ffmpeg", [...common, "-pass", "1", "-an", "-f", "mp4", process.platform === "win32" ? "NUL" : "/dev/null"]);
  execFileSync("ffmpeg", [...common, "-pass", "2", "-c:a", "aac", "-b:a", `${AUDIO_KBPS}k`, "-movflags", "+faststart", output]);
  console.log(`${output}: ${videoKbps} kbps video`);
}
