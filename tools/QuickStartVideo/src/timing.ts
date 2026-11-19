import scenesJson from "../scenes.json";
import narration from "./narration.json";

export type Scene = {
  id: string;
  kind?: "title";
  title?: string;
  subtitle?: string;
  image?: string;
  label?: string;
  focus?: [number, number];
  zoom?: number;
  text: string;
  caption?: string;
};

export const FPS = 30;
export const WIDTH = 1920;
export const HEIGHT = 1080;
/** Frames before a scene's narration starts. */
export const LEAD = 10;
/** Frames after a scene's narration ends. */
export const TAIL = 22;
/** Cross-fade length between scenes. */
export const TRANSITION = 15;

export const scenes = scenesJson as Scene[];
const seconds = narration as Record<string, number>;

export const audioFrames = (id: string) => Math.ceil((seconds[id] ?? 4) * FPS);

export const sceneFrames = (s: Scene) =>
  LEAD + audioFrames(s.id) + TAIL + (s.id === "outro" ? 45 : 0);

/** The 10-second "AIInnovative Opener" clip that plays before the intro card. */
export const OPENER_FRAMES = 300;

/** Opener plus every scene, minus the cross-fades between them. */
export const totalFrames = () =>
  OPENER_FRAMES + scenes.reduce((sum, s) => sum + sceneFrames(s), 0) - TRANSITION * scenes.length;
